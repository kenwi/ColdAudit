"""Denoise the lightmap atlas with OIDN, offline.

The bake leaves per-texel path-tracing noise, which is invisible in Blender but obvious
in game because the atlas is magnified over big surfaces.  OIDN works on the atlas as an
image: the background is filled first (nearest texel) so the black gaps do not bleed into
island edges, then the result is re-encoded with the sidecar's encode_scale.
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
    res = np.abs(lum - ndimage.median_filter(lum, size=3))
    return float(np.median(res[covered])), float(np.percentile(res[covered], 95))


def fill_pinholes(lin, max_px=8, window=5):
    """Replace small all-black islands inside lit areas with their neighbours.

    The bake leaves isolated zero texels (one texel, occasionally two) inside lit islands.
    OIDN keeps them: to an edge-preserving denoiser a lone black texel is detail, not
    noise.  In game each one is magnified into a soft black blob, which is far more
    visible than the noise the denoiser removed.  Only components up to ``max_px`` are
    touched, so genuine unlit regions (large black areas) are left alone.
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
          f"(p95 {highpass(lin)[1]:.5f})")

    # nearest valid texel fills the background so OIDN cannot see a hard black edge
    _, (iy, ix) = ndimage.distance_transform_edt(~valid, return_indices=True)
    filled = lin[iy, ix]

    with tempfile.TemporaryDirectory() as td:
        a, b = Path(td) / "in.pfm", Path(td) / "out.pfm"
        write_pfm(a, filled)
        cmd = ["oidnDenoise", "--hdr", str(a), "-o", str(b)]
        print(" ".join(cmd))
        subprocess.run(cmd, check=True)
        den = np.array(read_pfm(b), copy=True)

    den[~valid] = 0.0
    den = np.maximum(den, 0.0)
    # OIDN can round a few texels back down to zero; the atlas must not carry pinholes.
    den, holes2 = fill_pinholes(den)
    if holes2:
        print(f"post-denoise pinholes filled {holes2}")
    print(f"after denoise: highpass {highpass(den)[0]:.5f} (p95 {highpass(den)[1]:.5f})")

    enc = l2s(np.clip(den * scale, 0.0, 1.0))
    Image.fromarray(np.round(enc * 255).astype(np.uint8)).save(dst)
    print(f"wrote {dst}")


if __name__ == "__main__":
    main(*sys.argv[1:])
