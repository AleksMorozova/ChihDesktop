# Approved Chih application branding

The owner supplied and approved `chih-icon.png` and `chih-icon.ico` for official
ChihDesktop branding. The PNG composition is preserved unchanged. The supplied
ICO already contains readable 16, 24, 32, 48, 64, 128 and 256 px frames; no artwork
or ICO regeneration was necessary. Creation tooling for this branding is not
independently documented; do not assume the sprite-generation provenance applies
to this separate image. See ../../ASSET-LICENSE.md for the separate artwork terms.

The executable embeds this ICO as native Windows resources. WPF windows share
its largest decoded frame, and setup/shortcuts reference the approved icon.
`scripts/Test-AppIcon.ps1` verifies actual native frame bytes without executing
an EXE. The source PNG has only IHDR, IDAT and IEND chunks (no text metadata).
