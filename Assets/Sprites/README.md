# Chih production frames

These 15 illustrated PNGs are the current production Chih artwork. The owner confirms they were generated using OpenAI ChatGPT from photographs the owner owns of their own cat Chih, and authorizes inclusion in the public repository and official releases. `AnimationService` loads their embedded bytes directly; they are not generated from `ChihArtwork.Draw` during build or runtime. Private photographs, reference atlases and cleanup material remain excluded. MIT does not cover these images; see [the separate artwork license and provenance record](../../ASSET-LICENSE.md) for permitted bundled use and restrictions on standalone redistribution, modification, resale and commercial use.

| Animation | Files | Rendering |
| --- | --- | --- |
| Idle | Idle-00 through Idle-01 | Two PNG frames |
| WalkRight | WalkRight-00 through WalkRight-03 | Four PNG frames |
| WalkLeft | Same WalkRight frames | Horizontal mirroring |
| Sit | Sit-00 through Sit-01 | Two PNG frames |
| Sleep | Sleep-00 through Sleep-01 | Two PNG frames |
| Stretch | Stretch-00 through Stretch-02 | Three PNG frames, hold final frame |
| React | React-00 through React-01 | Two PNG frames |

Frames use a 320x260 transparent canvas. Walking has a shared y=244 ground baseline. The persistence refactor does not redraw, recrop, recolor or regenerate any production image. The relative SHA256 baseline in `tools/PersistenceChecks/sprite-baseline.json` protects the exact existing bytes; original artistic quality and historical extraction edges are not certified by a hash check.

Assets are embedded by `ChihDesktop.csproj`. `AnimationService` decodes all frames once and flips the complete canvas about its center. Movement remains independent of frame selection. Refer to the root README for non-generating validation commands. The legacy SpriteBuilder now permits output only below `artifacts/LegacySprites`, never this production directory.
