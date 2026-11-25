# Space-time ripple (full-screen heat effect) — reference recipe

This was an accidental but spectacular effect: the entire screen rippled and
shifted colours (including the afterburner rainbow) while ambushers moved. It is
**not** suitable as a per-drone cloak because it is a full-screen pass, but it is
worth keeping for anomalies, environmental hazards, or a deliberate “reality is
breaking” moment.

## How it works

- The game ships `SpaceTimeRipple` (`GraveyardOrbit.SpaceTimeRipple`) and a URP
  `FullScreenPassRendererFeature` that runs when `SpaceTimeRipple.Active`.
- `SpaceTimeRipple.Play(world, camera, kick)` starts/updates a damped spring
  centred on a world position. Repeated calls update the centre and add energy.
- Our ambusher director invoked it from moving cloaked drones, up to every
  `RippleInterval`, with the centre placed behind the drone.

## Settings that produced the effect

```ini
[Visuals]
CloakEffect = ripple
RippleKick = 0.45
RippleInterval = 0.10
RippleWakeOffset = 1.5
RippleMinSpeedPx = 10
```

Code path in this project: `AmbusherDirector.EmitRipple()` and
`AmbusherDrone.Update()` (motion-wake block), with `CloakEffect = ripple`.

## Notes / side effects

- It is **global**: every visible pixel, post-processing, and UI-adjacent effect
  can be affected, not just the area around the drone.
- Strong kicks make the screen look rainbow/psychedelic; lower `RippleKick` and
  raise `RippleInterval` for a subtle environmental wobble.
- It is an excellent candidate for a rare anomaly, an overdrive effect, a boss
  phase transition, or a “space-time damage” status. It is a poor fit for an
  enemy-specific cloak.

Not used by default; the default cloak is now `CloakEffect = trail` (a faint
localized wake).
