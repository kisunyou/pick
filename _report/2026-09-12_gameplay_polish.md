# Gameplay Polish - 2026-09-12

## Changes
- Coin shortage now uses UICoinShortPopup: Buy + coin icon, Watch Ad + video icon, Cancel.
- Buy opens the existing shop; Watch Ad uses the existing 500-coin reward flow.
- Ad availability and daily remaining count are displayed; unavailable/exhausted ads are disabled.
- GameMain rechecks readiness/daily limits and prevents overlapping ad requests; a failed/incomplete ad shows a notice without granting coins.
- Battle HUD height is 400 reference pixels (previously 313). Mission and random-box controls move below it.
- BossCamera fits visible skinned geometry and ally positions on boss spawn, reserves headroom, and synchronizes overlay-camera projection.
- Existing original textures are unchanged. All 24 _b/_r textures were replaced, preserving 512x512 size, RGB/RGBA format, alpha, protected eyes/red accessories, and Unity .meta/GUID.
- Suffixes _b and _r remain tier identifiers; body colors now vary by species.

## Variant Palette
| Species | _b | _r |
|---|---|---|
| Bear | Coral orange | Royal violet |
| Cow | Peach orange | Lavender |
| Duck | Mint green | Raspberry pink |
| Elephant | Soft violet | Golden yellow |
| Frog | Turquoise | Tangerine |
| Horse | Cobalt blue | Jade mint |
| Koala | Rose pink | Amber |
| Lion | Sky blue | Orchid |
| Monkey | Lavender | Teal |
| Octopus | Strawberry pink | Periwinkle |
| Panda | Apricot | Aqua |
| Pig | Mint | Sunflower |

Files: Assets/Resources/Prefabs/dollPrefabs/doll_{species}_full_{b,r}.tga, except doll_octopus_{b,r}.tga.

## Verification
- Popup rendered at 540x960, 540x1200, and 1024x768 with nonblank-pixel checks, valid icon references, and on-screen button bounds.
- All 36 boss models checked in a preview copy of Stage0, using the real parent scale and five samples of every controller animation; all sampled vertices stayed inside the viewport.
- Final Unity console: no errors.
- 24 texture round trips verified pixel-for-pixel, protected regions and alpha unchanged, original source hashes and .meta hashes preserved.
- No gameplay save reset, no actual purchase, and no live Android ad impression/reward test performed.
- Existing payment/cloud-save work and user mission-table changes preserved.

## Previews
- [Coin shortage popup](2026-09-12_coin_short_popup.png)
- [Battle framing](2026-09-12_battle_preview.png)
- [24 variant textures](2026-09-12_doll_variants.jpg)

## Recheck Menus
- Tools/Gameplay Polish/Validate And Preview
- Tools/Gameplay Polish/Preview Battle

## Image Production
Built-in image_gen editing was used once per variant (24 calls). Prompt:
"Edit this exact Unity doll UV texture atlas, not a picture of a doll. Recolor ONLY the main woven body cloth to [palette color]. Keep original fine weave and shading, no new texture, no redraw of UV layout or edges. Preserve original black eye and white glint, solid deep red accessory islands, dark spots, belly/face accents and identifying details. Moderate saturation, no neon. Original is 512x512."
Generated color references were applied with source-preserving color transfer; original UV details and protected pixels were retained.
