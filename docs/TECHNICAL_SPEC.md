# DRIFT CAREER — TECHNICAL REQUIREMENTS SPECIFICATION
Version 1.0 · 2026-11-05 · Proposed design, not implemented software

## 0. Environment and constraints
Development: MacBook Air Apple M2, 8 GB unified RAM. Unity 6 LTS editor locally, C#, URP, Unity Input System, Cinemachine, Unity Test Framework, Git + Git LFS. Prefer simple 3D Universal template, low-resolution textures, one active scene, modest draw calls, small test assets, no HDRP. Verify exact Unity package compatibility in Package Manager; do not hardcode unverified package versions. Android and iOS primary; PC/Steam and consoles later. VPS is optional for Git automation/backend, not a substitute for interactive Unity Editor without GPU. Unity must be installed and editor access confirmed before claiming scene compile/play results.

## 1. Architecture principles
- One repository with UnityProject/ and docs/. Keep game core platform-agnostic and testable without Unity scene dependencies where practical.
- Data-driven catalogs for vehicles, tracks, game modes and surface profiles; stable IDs, localization keys, implementation status (`ComingSoon`, `Playable`, `Hidden`), content version.
- Distinguish runtime physics state from visual mesh; no dependency of dynamics on a specific car FBX.
- Avoid premature abstraction: interfaces for input, save, content and platform services; concrete local implementations in MVP.
- No arbitrary remote code or untrusted Unity asset bundles. Keep third-party assets in separate folders and maintain attribution/license manifest.
- Fixed simulation timestep, input sampled/latched correctly, rendering interpolation, frame-rate-independent UI and camera.
- Separate editor-only tools from runtime, use assembly definitions where beneficial, do not overfragment on day one.

## 2. Proposed directory tree
```
DriftCareer/
  AGENTS.md
  README.md
  docs/
    GAME_IDEA.md
    TECHNICAL_SPEC.md
    CLAUDE_MASTER_PROMPT.md
    ARCHITECTURE.md
    PHYSICS_SPEC.md
    TASKS.md
    DECISIONS.md
    TEST_PLAN.md
    ASSET_LICENSES.md
  UnityProject/
    Assets/_DriftCareer/
      Core/ Data/ Vehicle/Physics/ Vehicle/Configs/
      Input/ Assists/ Gameplay/DriftScoring/ Gameplay/Training/
      Garage/ Career/ TrackCatalog/ TrackEditor/ SurfaceSystem/
      ContentPipeline/ Presentation/Cameras/ Presentation/UI/
      Presentation/Audio/ Platform/ Persistence/ Scenes/ Prefabs/
      Editor/ Tests/EditMode/ Tests/PlayMode/
    Assets/ThirdParty/
    Packages/
    ProjectSettings/
```
Follow existing Unity folder conventions if repository already exists; never destroy existing work to match this tree.

## 3. Domain entities and interfaces
- `VehicleDefinition`: stable ID, display name/localization, license status, default `VehiclePhysicsProfile`, visual prefab reference, compatible tuning slots.
- `VehiclePhysicsProfile`: mass, inertia approximation, wheelbase, track widths, CG, wheel radii, spring/damper rates, torque curve, ratios, diff model, tire model, steering limits, brake torques.
- `VehicleTuningState`: player adjustments with validated ranges and versioned schema.
- `VehicleRuntime`: physics state and four wheel contacts; render model driven by physics, not vice versa.
- `SurfaceProfile`: friction/rolling resistance, response vs slip and tire type, optional roughness, VFX/audio tags.
- `TrackDefinition`: stable ID, scene/addressable reference, surface profiles, collision boundaries, allowed modes, status, licensing metadata.
- `GameModeDefinition`: stable ID, localized title, unlock conditions, status, rules reference.
- `ObstacleDefinition`: prefab, footprint, collider type, mass/behavior, placement rules, mobile cost budget.
- `ObstacleLayout`: version, track ID, name, list of obstacle ID, pose, parameters; locally serialized and validated.
- `PlayerProfile`: garage ownership, selected vehicle, progress, settings and layout references, versioned and migration-aware.
- `IInputSource`, `IDriverAssist`, `ISaveService`, `IPlatformService`, `IContentCatalog`, `ITrackLoader`.

## 4. Vehicle simulation design
**Prototype A:** Rigidbody chassis + wheel contact raycasts, spring/damper vertical forces, simple continuous tire lateral/longitudinal force curves, drivetrain and brake torques. WheelCollider can be a temporary baseline but must not dictate architecture. Use consistent SI units and a fixed timestep candidate of 0.01 s (benchmark 0.02/0.01 on mobile). No direct yaw rotation tricks for drifting.

**Prototype B:** load-sensitive tire forces, combined-slip friction ellipse/circle, slip ratio and slip angle, static/kinetic transition, anti-roll, load transfer, steering geometry, clutch and engine inertia, limited-slip diff, tire/surface interaction. Define coordinate conventions and sign conventions explicitly. Clamp extreme forces, protect against NaN/Infinity, cap forces in pathological contacts and use deterministic-enough replay telemetry (cross-platform bitwise determinism is not assumed).

**Required tests:** zero throttle coast, straight acceleration/braking, steady circle, handbrake initiation, donut both directions, figure eight, left-right transitions, curb/obstacle impact, off-ground wheel, low speed/near zero division, 30/60/120 render FPS invariance, multiple fixed steps, save/load tuning, collision recovery. Log slip angle, wheel slip ratio, wheel normal loads, RPM, yaw rate, CG velocity, tire force and timestep.

**Assist system:** raw normalized driver commands → input smoothing and device mapping → assist adjustment (countersteer, throttle modulation, optional auto-clutch/shift) → vehicle controls. Presets Simulation, Balanced, Assisted; all have tunable limits. UI must disclose assistance. Competitive modes may enforce preset restrictions.

## 5. First location: Drift Academy
- Single playable scene, hangar near edge of lot, road/ground collision, open doorway large enough for vehicle, optional garage exit animation (not required for MVP), no scene transition.
- Initial car positioned inside hangar with parking brake or stationary startup. User explicitly starts driving; car exits through door under physics. Camera follows without clipping roof/walls.
- Asphalt ~250×250 m target (scale adjustable); parking practice zone, donut area, figure-eight, slalom, obstacle editor area, tire stacks and guardrails.
- Place cones, single tires, tire stacks, plastic barriers, bollards, clipping gates, chalk/paint markers. Use simple primitives/CC0 assets first. Static barriers can be immovable; cones dynamic rigidbodies with low-cost collisions. Do not instantiate hundreds of expensive rigidbodies on low-end phones.
- Reset car to safe spawn, reset obstacle layout independently, reset score, pause/resume, simple main menu.

## 6. Obstacle editor requirements
- Accessible while stationary or from pause; physics paused during edit.
- Top-down orthographic camera; mobile touch drag/pinch/rotate and PC mouse/keyboard.
- Select prefab from palette; ghost preview; snap to ground; rotate; place; move; duplicate; delete; undo/redo; object counter; placement validation and clear confirmation.
- Reject overlaps with hangar, vehicle spawn, exit lane, track boundary and disallowed zones; explain invalid placement.
- Presets and named layouts saved locally in versioned JSON (not executable serialization). Schema migration, corrupt file fallback, limits per object type, sanitization.
- Resume driving and collide with saved objects. Layout remains stable across restart. Later add share/export and server moderation, but not in MVP.

## 7. Game mode and track UI
Main menu sections: Academy (Playable), Free Drive (first Academy only), Drift School (Coming Soon until lessons work), Career (Coming Soon), Japan Touge (Coming Soon), Tokyo Parking (Coming Soon), Japan Circuits (Coming Soon), RDS Tracks (Coming Soon), Winter/Baikal Ice (Coming Soon), Tandem AI (Coming Soon), Online (Coming Soon), Community Cars/Tracks (Coming Soon), Garage (Coming Soon until implemented).
Use `FeatureAvailability` enum/status in data, not hard-coded button conditions. Coming Soon buttons show details but do not load empty scenes. All UI strings localized (at least RU and EN keys). Avoid claiming official licensing.

## 8. Surfaces and winter roadmap
Surface profiles for dry/wet asphalt, packed/loose snow, smooth ice, studded ice. Tires have compound-specific response; tire × surface × temperature lookup may be extended later. Winter gameplay shares core physics and scoring, not a separate arcade controller. Baikal-inspired scene is future content, not a physical representation or real driving recommendation.

## 9. Content and licensing
Start with CC0 or licensed assets from Kenney, Quaternius, Poly Haven and carefully checked Unity Asset Store items. Record source URL, creator, license text/date, attribution requirements, commercial redistribution rules and modifications in `ASSET_LICENSES.md`. Avoid ripped cars/tracks from other games, branded logos without permission and unlicensed film replicas. Real RDS branding, track layouts, manufacturer marks and licensed scans require legal review and agreements as applicable. Hunyuan3D or other AI tools require version/service-specific commercial and territorial license review; generated mesh still needs technical cleanup.

**Future UGC pipeline:** source upload FBX/GLB + manifest + rights attestation → quarantine/object storage → malware/type/size scan → geometry/material validator → human moderation → platform-specific build by trusted pipeline → signed catalog → approved distribution → report/takedown. Never execute imported scripts; never load player-submitted bundles directly. Separate private custom orders from publicly shareable catalog rights.

## 10. Save, economy and backend
MVP: offline versioned local JSON saves with atomic write, backup, corruption recovery and migration tests. Future: authenticated accounts, entitlements, cloud sync, purchases, UGC catalog, moderation, cross-progression, anti-cheat and multiplayer. Use service interfaces but no actual backend requirement in first playable. VPS can later host API, PostgreSQL, object storage and job workers after security design.

## 11. Performance budgets and profiling
8GB Mac: close heavy apps, low Editor game-view resolution, one scene, low texture resolution, simple shadows, avoid running Blender/large browsers alongside Unity. Mobile targets: 60 FPS on selected midrange devices where achievable, 30 FPS fallback; performance is measured on hardware, not promised. Track CPU main thread, physics cost, GPU frame time, memory, draw calls, overdraw, GC allocations, thermal throttling. Use object pooling, static batching/instancing as suitable, LODs, compressed textures, simple collision meshes. Benchmark representative Android and iPhone devices; editor FPS is not proof of mobile performance.

## 12. Development work packages — five initial assignments
**Task 1 — Foundation:** inspect repo, confirm Unity version, write AGENTS/architecture/roadmap/decisions/tasks, configure Git ignore/LFS, create clean URP project, basic menus, test infrastructure. Acceptance: editor opens with zero compile errors; scene can play; clean Git status excluding generated files.

**Task 2 — Vehicle prototype:** car placeholder, Rigidbody chassis, 4 wheel contact modules, basic spring/damper/tire/drivetrain, handbrake, telemetry, reset. Acceptance: accelerate, steer, brake, donut/figure-eight prototype without NaNs; edit-mode math tests pass; no unrealistic teleport/yaw hack.

**Task 3 — Input and assists:** keyboard and gamepad, mobile touch UI, optional tilt, Simulation/Balanced/Assisted presets and settings. Acceptance: same vehicle works across input types; no hard-coded platform assumptions; settings persist; touch build tested on device before claiming success.

**Task 4 — Gameplay/UI/catalog:** speed/RPM/gear/drift angle HUD, score/combo prototype, training challenges, all future mode/track cards with «Скоро», localized labels. Acceptance: working mode starts Academy; unavailable modes do not load nonexistent content; score rules documented and testable.

**Task 5 — Academy + obstacle editor:** hangar exit, asphalt lot, barriers/cones/tires, editor palette/top-down placement/rotation/delete/undo/save/load, collision and restart. Acceptance: drive out of hangar, place 20+ obstacles (configurable), save/reload layout, collide without simulation instability, no placement blocking exit.

## 13. Phased roadmap (estimates, not commitments)
Phase 0: 1–2 days environment and project bootstrapping. Phase 1: 1–2 weeks basic drivable prototype. Phase 2: 3–8+ weeks credible drift physics iteration. Phase 3: 2–4 weeks Academy editor and training UX. Phase 4: 4–8 weeks content, garage, mobile controls and optimization. Phase 5: QA, legal/store readiness, limited beta and mobile launch (overall several months, often 5–8+). PC/console/online/UGC follow after validation and funding. Do not parallelize dependent tasks prematurely.

## 14. Quality gates
- Compile success in installed Unity version; missing Unity Editor must be reported honestly.
- No uncaught exceptions or NaNs during a 15-minute practice run.
- Save/load survives restart and malformed save input.
- Keyboard driving on Mac and touch driving on at least one physical phone.
- No inaccessible garage exit; no obstacle placed on player spawn.
- Stable physics across representative rendering FPS.
- Mobile performance measured and documented with actual device, resolution, build and settings.
- All assets have recorded licenses; no unauthorized brands or ripped game content.
- Unimplemented mode buttons labeled «Скоро» and disabled.

## 15. Immediate operator workflow
1. Install Unity Hub + current Unity 6 LTS Apple Silicon + Android Build Support (SDK/NDK/OpenJDK), iOS module later as needed.
2. Create URP project locally; open and wait for import; commit clean baseline.
3. Place these three docs in `docs/` and give Claude Code access to local repository.
4. Ask Claude to inspect actual project before proposing changes; review Task 1 plan.
5. Execute tasks one at a time; launch Unity Play mode after each milestone; capture logs/telemetry.
6. Use VPS only for supplementary automation; GitHub for source control. Never put secrets in Git.
