"""Denoise the lightmap atlas with OIDN, offline.

The bake leaves per-texel path-tracing noise, which is invisible in Blender but obvious
in game because the atlas is magnified over big surfaces. Shadows are the hard case:
irradiance there is 50-100x below the bright floors, so a plain HDR denoise spends its
capacity on the highlights and leaves the dark band as blotches (measured: ~8% of
texels below L=0.02 are <0.5x their local median, against <0.5% in the mid-tones).

Pipeline:
  1. Decode the sRGB PNG with the sidecar encode_scale.
  2. Fill exact-black pinholes inside lit islands (OIDN treats them as detail).
  3. Nearest-fill the atlas background so island edges do not smear.
  4. Compress with asinh so shadows and highlights share a similar dynamic range,
     then run OIDN's ``RTLightmap`` filter (not the default beauty ``RT``).
  5. Expand back, restore the background, fill any pinholes OIDN re-zeroed, and
     lightly median-filter only the dark band so residual shadow blotches flatten
     without touching the lit floors.
"""
import json
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

HERE = Path(__file__).resolve().parent
DEFAULT = HERE.parent / "content/levels/office/office_lightmap.png"

# asinh knee: lin=1 maps near the middle of the compressed range. Office p99 sits
# around 10-15 irradiance/pi; this keeps the bright panels from dominating OIDN.
COMPRESS_SCALE = 4.0

# After OIDN, soften only texels dimmer than this (linear irradiance/pi).
SHADOW_SMOOTH_Luma = 0.12
SHADOW_SMOOTH_SIZE = 5


def s2l(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def l2s(c):
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(np.maximum(c, 0), 1 / 2.4) - 0.055)


def write_pfm(path, img):
    h, w, _ = img.shape
    with open(path, "wb") as f:
        f.write(b"PF\n%d %d\n-1.0\n" % (w, h))
        f.write(img[::-1].astype("<f4").tobytes())   # PFM rows run bottom-up


def read_pfm(path):
    with open(path, "rb") as f:
        assert f.readline().strip() == b"PF"
        w, h = map(int, f.readline().split())
        scale = float(f.readline())
        data = np.frombuffer(f.read(), dtype="<f4" if scale < 0 else ">f4")
        return data.reshape(h, w, 3)[::-1]


def highpass(lin):
    """Median absolute difference from a 3x3 median, on covered texels."""
    lum = lin.mean(axis=2)
    covered = lum > 0
    if not covered.any():
        return 0.0, 0.0
    res = np.abs(lum - ndimage.median_filter(lum, size=3))
    return float(np.median(res[covered])), float(np.percentile(res[covered], 95))


def shadow_blotch(lin, lo=0.0, hi=0.05):
    """Fraction of dark-band texels below half their 9x9 median."""
    lum = lin.mean(axis=2)
    band = (lum > lo) & (lum < hi)
    if not band.any():
        return 0.0
    med = ndimage.median_filter(lum, size=9)
    return float((lum[band] < 0.5 * med[band]).mean())


def fill_pinholes(lin, max_px=32, window=5):
    """Replace small all-black islands inside lit areas with their neighbours.

    The bake leaves isolated zero texels inside lit islands - and in shadows,
    whole clusters of them. OIDN keeps them: to an edge-preserving denoiser a
    black texel is detail. Only components up to ``max_px`` are touched, so
    genuine unlit gutters (large black areas) are left alone.
    """
    black = lin.max(axis=2) == 0
    lab, n = ndimage.label(black)
    if not n:
        return lin, 0
    sizes = ndimage.sum(black, lab, range(1, n + 1))
    small = np.isin(lab, np.nonzero(sizes <= max_px)[0] + 1)
    if not small.any():
        return lin, 0
    out = lin.copy()
    r = window // 2
    ys, xs = np.nonzero(small)
    for y, x in zip(ys, xs):
        y0, y1 = max(0, y - r), min(lin.shape[0], y + r + 1)
        x0, x1 = max(0, x - r), min(lin.shape[1], x + r + 1)
        patch = out[y0:y1, x0:x1]
        good = patch.max(axis=2) > 0
        if good.any():
            out[y, x] = np.median(patch[good], axis=0)
    return out, int(small.sum())


def compress(lin):
    return np.arcsinh(np.maximum(lin, 0.0) * COMPRESS_SCALE)


def expand(comp):
    return np.sinh(np.maximum(comp, 0.0)) / COMPRESS_SCALE


def smooth_shadows(lin, valid):
    """Median-filter only the dark band; leave lit surfaces alone."""
    lum = lin.mean(axis=2)
    shadow = valid & (lum > 0) & (lum < SHADOW_SMOOTH_Luma)
    if not shadow.any():
        return lin, 0
    # Filter a filled image so island edges do not pull toward black.
    _, (iy, ix) = ndimage.distance_transform_edt(~valid, return_indices=True)
    filled = lin[iy, ix]
    blurred = np.empty_like(filled)
    for c in range(3):
        blurred[:, :, c] = ndimage.median_filter(filled[:, :, c], size=SHADOW_SMOOTH_SIZE)
    out = lin.copy()
    out[shadow] = blurred[shadow]
    return out, int(shadow.sum())


def main(src=DEFAULT, dst=None, stats_only=False):
    src = Path(src)
    dst = Path(dst) if dst else src.with_name(src.stem + "_denoised" + src.suffix)
    side = json.loads(src.with_suffix(".json").read_text())
    scale = side["encode_scale"]
    raw = np.asarray(Image.open(src).convert("RGB"), dtype=np.float32) / 255.0
    lin = s2l(raw) / scale
    lin, holes = fill_pinholes(lin)
    valid = lin.max(axis=2) > 0
    print(f"{src.name}: {raw.shape[1]}x{raw.shape[0]} covered {valid.mean()*100:.1f}% "
          f"pinholes filled {holes} highpass {highpass(lin)[0]:.5f} "
          f"(p95 {highpass(lin)[1]:.5f}) shadow-blotch[0,0.05) {shadow_blotch(lin)*100:.2f}%")

    if stats_only:
        return

    # nearest valid texel fills the background so OIDN cannot see a hard black edge
    _, (iy, ix) = ndimage.distance_transform_edt(~valid, return_indices=True)
    filled = lin[iy, ix]
    compressed = compress(filled)

    with tempfile.TemporaryDirectory() as td:
        a, b = Path(td) / "in.pfm", Path(td) / "out.pfm"
        write_pfm(a, compressed.astype(np.float32))
        # RTLightmap is trained for bake atlases; RT is a beauty-pass filter and
        # under-denoises the low end of an HDR lightmap.
        cmd = ["oidnDenoise", "-f", "RTLightmap", "--hdr", str(a), "-o", str(b)]
        print(" ".join(cmd))
        subprocess.run(cmd, check=True)
        den = expand(np.array(read_pfm(b), copy=True))

    den[~valid] = 0.0
    den = np.maximum(den, 0.0)
    den, holes2 = fill_pinholes(den)
    if holes2:
        print(f"post-denoise pinholes filled {holes2}")
    den, n_shadow = smooth_shadows(den, valid)
    print(f"shadow-smooth {n_shadow} texels (L < {SHADOW_SMOOTH_Luma})")
    print(f"after denoise: highpass {highpass(den)[0]:.5f} (p95 {highpass(den)[1]:.5f}) "
          f"shadow-blotch[0,0.05) {shadow_blotch(den)*100:.2f}%")

    enc = l2s(np.clip(den * scale, 0.0, 1.0))
    Image.fromarray(np.round(enc * 255).astype(np.uint8)).save(dst)
    print(f"wrote {dst}")


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if a != "--stats-only"]
    main(*args, stats_only="--stats-only" in sys.argv)
