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
- `content/levels/<n>/` - numbered levels (manifest + sector/portal files)

## Level format

Each level lives in `content/levels/<int>/`:

```text
content/levels/1/
  level.json                 # manifest (ids, spawn, file lists)
  sectors/
    room_a.json              # sector metadata (+ optional model)
    room_a.glb               # optional mesh referenced by sector json
  portals/
    portal_a_b.json          # portal connectivity + corners
```

`level.json` lists relative paths to sector and portal files. `JsonLevelLoader` loads them into `LevelData` (and loads sector models when `model` is set).

## Working title

Cold Audit - off-hours security auditor / attacker wing infiltration.
