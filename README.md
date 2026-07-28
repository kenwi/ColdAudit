# Cold Audit

First-person stealth / systems-infiltration prototype built with **C#** and **Raylib-cs**.

Organized as **vertical slices** under `src/Features/`, with a thin `Shared/` kernel.

## Run

```bash
dotnet run --project src
```

## Layout

- `src/GameApp.cs` - composition root and frame loop
- `src/Shared/` - world blackboard, input, contracts, math
- `src/Features/` - one folder per gameplay capability
- `content/levels/` - Blender exports (glb + sidecar JSON)

## Working title

Cold Audit - off-hours security auditor / attacker wing infiltration.
