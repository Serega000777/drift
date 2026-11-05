# DRIFT CAREER — GAME VISION & PRODUCT DESIGN
Version 1.0 · 2026-11-05 · Product vision (not a claim of implemented features)

## 1. Product thesis
Drift Career is a cross-platform driving game built around believable rear-wheel-drive vehicle dynamics with accessible input assistance. First release: Android/iOS. Subsequent releases: Windows/Steam, then consoles subject to platform-holder approval. One shared simulation and gameplay model; presentation, input, and platform services differ.

**Player fantasy:** Start with a modest drift car in an industrial hangar, drive out onto an asphalt practice lot, master donuts, figure-eights, transitions and clipping points, build a dream drift car, progress through competitions, and eventually drive on famous circuits and original Japanese-inspired mountain roads, plus winter ice.

**Design pillars:** (1) Driving feels credible, not scripted; (2) approachable on touchscreens, deep with a controller or wheel; (3) player creativity through tuning, obstacle placement, liveries, cars and later tracks; (4) progression through skill rather than pay-to-win; (5) mobile-first performance, long-term PC/console readiness.

## 2. Audience and platforms
- Mobile: novice-to-intermediate drift fans, short sessions, assist presets, touch/tilt/controller.
- PC: enthusiasts, higher-fidelity graphics, deeper controls, wheels/pedals when supported and validated.
- Console: later port, controller-first UX, platform certification and approved SDKs.
- Modes must work offline where practical; cloud account, cross-progression and multiplayer are future features.

## 3. First playable: Drift Academy
Single seamless scene: industrial hangar with a stationary RWD car and openable/open garage door; player takes control, drives out without loading screen into ~250×250 m asphalt test ground (dimensions adjustable). Outdoor lot includes marked free-drive space, donuts ring, figure-eight practice, slalom and a customizable obstacle zone. Perimeter: guardrails, tire walls and safe run-off. Realistic collision responses within performance limits. No pedestrians or road traffic in first playable.

**Player loop:** spawn in hangar → start engine → drive out → practice → pause/open top-down layout editor → place cones, tire stacks, plastic barriers, clipping gates, poles and markers → rotate/move/delete/undo → validate layout → save a named preset → return to driving → earn feedback/points → restart/repair car → repeat.

**Obstacle rules:** ground snapping, rotation, collision checks, no placement inside car or hangar exit, object count limits based on device, clear-all with confirmation, save/load locally, editor mode freezes car physics safely. Cones can topple, tires and barriers collide with car; obstacles cannot cause physics explosions. Presets: empty lot, donut, figure-eight, slalom, clipping-zone drill, custom.

**Camera and HUD:** chase camera, hood camera, optional cockpit where mesh permits; speed, RPM, gear, drift angle, score/combo, assist preset, reset vehicle and pause. First playable can use a placeholder car and debug UI.

## 4. Driving feel and controls
- Common physical model: rigid body, tire forces, suspension, longitudinal/lateral slip, load transfer, torque curve, clutch, gearbox, differential, handbrake, surfaces.
- Three assistance modes: Simulation (minimal), Balanced (controller-friendly), Assisted (mobile-friendly). Assist layers transform input; they do not teleport or artificially rotate the vehicle.
- Mobile: touch steering wheel or buttons, optional tilt, throttle/brake, handbrake, auto/manual gears; sensitivity and handedness options.
- PC: keyboard, controller; wheel/pedals and force feedback later, only when hardware/API testing is possible.
- Surface types: dry asphalt, wet asphalt, packed snow, loose snow, ice. Tire compounds including studded winter tires. Coefficients calibrated, not treated as universal constants.

## 5. Cars and garage
**Launch aspiration:** three RWD cars with distinct mass/power/handling, subject to content readiness. Prototype: one placeholder. Future wishlist: BMW E36/E46, Nissan Silvia S13/S14/S15, Toyota Supra, Mazda RX-7 and other recognizable drift cars. Real manufacturer marks and recognizable vehicle designs may require permission; never ship unlicensed game rips or imply official affiliation.

**Garage:** select car; inspect and rotate; tune paint, wheels, body kit, liveries, suspension height/stiffness/damping, alignment, steering angle, tire type/pressure, engine torque/power, gear ratios, final drive, differential lock, brake balance. Physics tuning should affect handling in measurable ways. Separate visuals, physical profiles, tuning and ownership records.

**Player-created cars:** future submission of FBX/GLB + metadata and evidence of rights; sandboxed import pipeline, validation of mesh/material/scale/wheel pivots/LOD/performance, human moderation, controlled distribution of server-built platform assets. No arbitrary scripts, executable code or player-supplied AssetBundles. Private use and public sharing are distinct permissions.

**Custom digital twin orders:** user supplies photos from multiple angles, dimensions, wheelbase, modifications and proof of rights to submitted media. Team quotes and produces game-ready model; tiers might include visual-only, detailed exterior/interior, and handling approximation from specifications. Do not promise measured real-world dynamics from photos. AI-generated base meshes may require retopology, wheel separation, UVs, LODs and licensing review.

## 6. Track and mode catalog
Catalog must exist in data/UI before content is built. All unimplemented entries display localized **«Скоро»** and cannot be launched. Do not fake downloadable tracks or events.

**Track families:**
1. Drift Academy — industrial hangar and asphalt practice lot (MVP playable).
2. Parking Drift — original multi-storey parking structure, night lighting, ramps and pillars (future).
3. Japan Touge — original mountain pass with hairpins, elevation changes, tunnels and guardrails; Tokyo Drift atmosphere without copying film assets (future).
4. Japan circuits — potential licensed/authorized Ebisu, Nikko, Tsukuba and others (future).
5. RDS / professional tracks — potential authorized layouts at Moscow Raceway, ADM Raceway, Igora Drive, NRing, Red Ring and others; layouts/seasonal clipping zones separately versioned (future).
6. Winter Drift — snow training, icy circuits, frozen-lake-inspired Baikal course (future); do not imply real-world ice access is safe.
7. Original city/industrial, mountain, airport and test tracks (future).

**Game modes:** Free Drive, Drift School, Score Attack, Slalom, Donuts, Figure Eight, Clipping Challenge, Career, Time Attack (optional), Tandem AI, Online Tandem, Community Tracks, Seasonal Winter League. First playable: Free Drive and local training drills. Other modes «Скоро» until actually playable.

**Track creation:** first an internal authoring workflow and obstacle layout editor. Later user-created full tracks with curves, terrain, barriers, checkpoints, surface metadata, moderation and distribution. Real-world track reconstruction may use authorized surveys, photos, drone photogrammetry or LiDAR; use legally sourced data and separate venue, brand, map imagery and series permissions.

## 7. Career and progression
Begin as novice at Academy → drift license lessons → amateur cups → professional events → regional/international leagues. Training objectives include steady donut, controlled entry, figure-eight, transitions, proximity to clipping markers, safe speed/angle. Score should reward angle, speed, line and continuity; penalize collisions, spins and leaving course. Transparent scoring with replay/telemetry support; no misleading simulation of official RDS judging without agreement.

## 8. Economy and monetization
Mobile free-to-play candidate: earnable in-game currency, cosmetic liveries/wheels/visual packs, optional paid content and rewarded ads outside driving (if used). Avoid pay-to-win competitive handling advantages. PC/console may be premium game or different DLC model; evaluate after audience tests. App-store purchase rules, creator payouts, taxes, moderation and regional availability need dedicated legal/product review before launch.

## 9. Multiplayer and community (future)
Server-authoritative competition state where feasible; anti-cheat and replay validation; lobbies, tandem lead/chase, leaderboards, seasonal events, approved custom cars and tracks, creator profiles. Do not build networking in MVP. User-generated content requires reporting, takedowns, moderation, rights attestations and privacy protections.

## 10. Audio/visual direction
Grounded automotive look, not generic neon arcade. Industrial academy in daylight first; later Japanese night, wet asphalt, winter ice, tire smoke, skid marks, engine load-dependent audio, turbo/backfire where appropriate. Adaptive URP quality tiers. Original brand identity; avoid unlicensed film logos, soundtrack, game-ripped assets, and trademarks in marketing.

## 11. Release gates
**P0 technical proof:** one car drives and drifts reliably on Academy lot, can exit hangar, obstacles collide, editor places/saves presets, usable mobile controls, profiler measurements.
**P1 vertical slice:** polished garage, lessons, score, 1–3 licensed/original cars, at least one polished location, reliable save, testable mobile builds.
**P2 mobile release:** content, accessibility, onboarding, monetization compliance, analytics/privacy, device QA, store approval.
**P3 PC:** graphics, wheel support, performance and store readiness.
**P4 console:** partner approval, platform SDKs, certification, budgeted port.

## 12. Non-goals for MVP
No licensed RDS integration, online, user-uploaded executable content, paid marketplace, full photorealistic Baikal, all Japanese circuits, console SDKs or advanced damage simulation. Architecture must not confuse planned with shipped.
