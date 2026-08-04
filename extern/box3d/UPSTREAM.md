# Box3D C# bindings (vendored)

Engine-free Interop + Core layers and prebuilt natives from:

- **Binding:** https://github.com/TomMoore515/Box3DUnity
- **Pinned commit:** `d166b8235ffa1e5d32536bd01ae902ad65862be2`
- **Upstream engine:** https://github.com/erincatto/box3d (v0.1.0, `BOX3D_DOUBLE_PRECISION` ABI)
- **License:** MIT (see `LICENSE.md`)

Unity-only assemblies (`Box3D.Unity`), samples, and tests were not vendored.

To refresh: copy `Runtime/Interop/*.cs`, `Runtime/Core/*.cs`, and
`Runtime/Plugins/{Linux,Windows}/x86_64/*` from a newer Box3DUnity checkout,
then update this pin.
