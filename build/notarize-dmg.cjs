// electron-builder `afterAllArtifactBuild` hook.
//
// electron-builder notarizes + staples the .app (and the .zip the updater uses),
// but it does NOT notarize the DMG container. Beta users who download the DMG
// would otherwise hit a Gatekeeper warning on first open. This hook notarizes +
// staples each DMG, then patches latest-mac.yml so the DMG's hash/size match the
// (re-stapled) file. Skips cleanly when notary creds aren't present (e.g. a
// local signing-only build), so it never breaks non-release builds.
const { execFileSync } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

module.exports = async (context) => {
  if (process.platform !== 'darwin') return [];

  const { APPLE_API_KEY, APPLE_API_KEY_ID, APPLE_API_ISSUER } = process.env;
  if (!APPLE_API_KEY || !APPLE_API_KEY_ID || !APPLE_API_ISSUER) {
    console.log('[notarize-dmg] notary credentials not set — skipping DMG stapling');
    return [];
  }

  const dmgs = (context.artifactPaths || []).filter((p) => p.endsWith('.dmg'));
  for (const dmg of dmgs) {
    console.log(`[notarize-dmg] notarizing ${path.basename(dmg)} …`);
    execFileSync(
      'xcrun',
      [
        'notarytool', 'submit', dmg,
        '--key', APPLE_API_KEY,
        '--key-id', APPLE_API_KEY_ID,
        '--issuer', APPLE_API_ISSUER,
        '--wait'
      ],
      { stdio: 'inherit' }
    );
    execFileSync('xcrun', ['stapler', 'staple', dmg], { stdio: 'inherit' });
    patchLatestYml(path.join(context.outDir, 'latest-mac.yml'), dmg);
  }

  return [];
};

const sha512Base64 = (file) =>
  crypto.createHash('sha512').update(fs.readFileSync(file)).digest('base64');

// Stapling rewrites the DMG, so its hash/size in latest-mac.yml are now stale.
// The updater uses the .zip, not the DMG, but keep the manifest accurate.
function patchLatestYml(ymlPath, dmg) {
  if (!fs.existsSync(ymlPath)) return;
  const name = path.basename(dmg);
  const size = fs.statSync(dmg).size;
  const sha = sha512Base64(dmg);
  const lines = fs.readFileSync(ymlPath, 'utf8').split('\n');
  for (let i = 0; i < lines.length; i += 1) {
    if (lines[i].includes(`url: ${name}`)) {
      for (let j = i + 1; j < Math.min(i + 4, lines.length); j += 1) {
        if (lines[j].includes('sha512:')) lines[j] = lines[j].replace(/sha512:.*/, `sha512: ${sha}`);
        if (lines[j].includes('size:')) lines[j] = lines[j].replace(/size:.*/, `size: ${size}`);
      }
      break;
    }
  }
  fs.writeFileSync(ymlPath, lines.join('\n'));
  console.log(`[notarize-dmg] patched latest-mac.yml for ${name}`);
}
