// Upload release artifacts to the Cloudflare R2 bucket that serves auto-updates.
// Run with: npm run release:upload [-- --dry-run] [-- --dir app/macOS]
//
// Uploads the update feed (latest-mac.yml / latest.yml) plus the artifacts for
// the current package.json version (.zip/.dmg/.exe + .blockmap) to the bucket
// root, where electron-updater expects them. R2 is S3-compatible; requests are
// signed with AWS Signature V4 using only Node built-ins — no SDK needed.
//
// Credentials (an R2 API token) are read from the environment, or from a
// git-ignored .env.r2 file in the repo root:
//   R2_ACCESS_KEY_ID / R2_SECRET_ACCESS_KEY   (AWS_* names also accepted)
// Optional overrides: R2_ACCOUNT_ID, R2_BUCKET, R2_PUBLIC_URL.
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..');

// Defaults for this project's bucket (documented in windowsInstructions.md).
const DEFAULTS = {
  accountId: 'b9d2073cb737c168f67215026b0c9b64',
  bucket: 'heracles',
  publicUrl: 'https://pub-622aa86bc99340e3ac5b01cb86b7d752.r2.dev/'
};

// ── Config ─────────────────────────────────────────────────────────────────
const loadDotEnv = () => {
  const file = path.join(ROOT, '.env.r2');
  if (!fs.existsSync(file)) return;
  for (const line of fs.readFileSync(file, 'utf8').split('\n')) {
    const match = line.match(/^\s*(?:export\s+)?([A-Z0-9_]+)\s*=\s*"?([^"#]*)"?\s*$/);
    if (match && !(match[1] in process.env)) {
      process.env[match[1]] = match[2].trim();
    }
  }
};

loadDotEnv();

const args = process.argv.slice(2);
const dryRun = args.includes('--dry-run');
const dirFlagIndex = args.indexOf('--dir');
const explicitDir = dirFlagIndex >= 0 ? args[dirFlagIndex + 1] : null;

const accountId = process.env.R2_ACCOUNT_ID || DEFAULTS.accountId;
const bucket = process.env.R2_BUCKET || DEFAULTS.bucket;
const publicUrl = (process.env.R2_PUBLIC_URL || DEFAULTS.publicUrl).replace(/\/?$/, '/');
const accessKeyId = process.env.R2_ACCESS_KEY_ID || process.env.AWS_ACCESS_KEY_ID;
const secretAccessKey = process.env.R2_SECRET_ACCESS_KEY || process.env.AWS_SECRET_ACCESS_KEY;

const { version } = require(path.join(ROOT, 'package.json'));

// ── Artifact discovery ─────────────────────────────────────────────────────
const FEED_FILES = new Set(['latest-mac.yml', 'latest.yml']);
const ARTIFACT_RE = /\.(zip|dmg|exe|blockmap)$/i;

const findArtifactDir = () => {
  if (explicitDir) return path.resolve(ROOT, explicitDir);
  // Whichever platform output dir holds an update feed is the release to ship.
  for (const candidate of ['app/macOS', 'app/windows']) {
    const dir = path.join(ROOT, candidate);
    if (
      fs.existsSync(dir) &&
      fs.readdirSync(dir).some((name) => FEED_FILES.has(name))
    ) {
      return dir;
    }
  }
  return null;
};

const contentTypeFor = (name) => {
  if (name.endsWith('.yml')) return 'text/yaml';
  if (name.endsWith('.zip')) return 'application/zip';
  if (name.endsWith('.dmg')) return 'application/x-apple-diskimage';
  return 'application/octet-stream';
};

// Feeds must never be cached stale (clients poll them); artifacts are
// versioned in their filename and safe to cache forever.
const cacheControlFor = (name) =>
  name.endsWith('.yml') ? 'no-cache' : 'public, max-age=31536000, immutable';

const collectFiles = (dir) => {
  const names = fs.readdirSync(dir);
  const feeds = names.filter((name) => FEED_FILES.has(name));
  const artifacts = names.filter((name) => ARTIFACT_RE.test(name));
  const stale = artifacts.filter((name) => !name.includes(version));
  const current = artifacts.filter((name) => name.includes(version));
  return { feeds, current, stale };
};

// ── AWS Signature V4 (R2's S3 API) ─────────────────────────────────────────
const sha256Hex = (data) => crypto.createHash('sha256').update(data).digest('hex');
const hmac = (key, data) => crypto.createHmac('sha256', key).update(data).digest();

// RFC 3986 encoding: encodeURIComponent, plus the characters it leaves bare.
const rfc3986 = (segment) =>
  encodeURIComponent(segment).replace(
    /[!'()*]/g,
    (c) => `%${c.charCodeAt(0).toString(16).toUpperCase()}`
  );

const putObject = async (key, body, contentType, cacheControl) => {
  const host = `${accountId}.r2.cloudflarestorage.com`;
  const canonicalUri = `/${rfc3986(bucket)}/${rfc3986(key)}`;
  const now = new Date();
  const amzDate = now.toISOString().replace(/[-:]|\.\d{3}/g, '');
  const dateStamp = amzDate.slice(0, 8);
  const payloadHash = sha256Hex(body);

  const headers = {
    'cache-control': cacheControl,
    'content-type': contentType,
    host,
    'x-amz-content-sha256': payloadHash,
    'x-amz-date': amzDate
  };
  const signedHeaderNames = Object.keys(headers).sort();
  const canonicalHeaders = signedHeaderNames.map((h) => `${h}:${headers[h]}\n`).join('');
  const signedHeaders = signedHeaderNames.join(';');

  const canonicalRequest = [
    'PUT',
    canonicalUri,
    '',
    canonicalHeaders,
    signedHeaders,
    payloadHash
  ].join('\n');

  const scope = `${dateStamp}/auto/s3/aws4_request`;
  const stringToSign = ['AWS4-HMAC-SHA256', amzDate, scope, sha256Hex(canonicalRequest)].join('\n');

  const kDate = hmac(`AWS4${secretAccessKey}`, dateStamp);
  const kRegion = hmac(kDate, 'auto');
  const kService = hmac(kRegion, 's3');
  const kSigning = hmac(kService, 'aws4_request');
  const signature = crypto.createHmac('sha256', kSigning).update(stringToSign).digest('hex');

  const authorization =
    `AWS4-HMAC-SHA256 Credential=${accessKeyId}/${scope}, ` +
    `SignedHeaders=${signedHeaders}, Signature=${signature}`;

  const response = await fetch(`https://${host}${canonicalUri}`, {
    method: 'PUT',
    headers: { ...headers, authorization },
    body
  });
  if (!response.ok) {
    const detail = (await response.text()).slice(0, 400);
    throw new Error(`PUT ${key} failed: ${response.status} ${detail}`);
  }
};

// ── Main ───────────────────────────────────────────────────────────────────
const main = async () => {
  const dir = findArtifactDir();
  if (!dir) {
    console.error(
      'No release output found. Run `npm run release:mac` / `npm run release:win` first,\n' +
        'or pass --dir <path> to the artifacts directory.'
    );
    process.exit(1);
  }

  const { feeds, current, stale } = collectFiles(dir);
  if (feeds.length === 0) {
    console.error(`No latest-mac.yml / latest.yml in ${dir} — not a release build.`);
    process.exit(1);
  }
  if (current.length === 0) {
    console.error(`No artifacts for version ${version} in ${dir}. Is the version bumped?`);
    process.exit(1);
  }
  for (const name of stale) {
    console.warn(`skipping (not version ${version}): ${name}`);
  }

  // Feeds go last: clients only see the new version once its artifacts exist.
  const uploads = [...current, ...feeds];

  console.log(`Release ${version} → r2://${bucket} (${uploads.length} files from ${path.relative(ROOT, dir)})`);
  if (dryRun) {
    for (const name of uploads) console.log(`  would upload: ${name}`);
    return;
  }

  if (!accessKeyId || !secretAccessKey) {
    console.error(
      'Missing R2 credentials. Set R2_ACCESS_KEY_ID and R2_SECRET_ACCESS_KEY in the\n' +
        'environment or in a git-ignored .env.r2 file at the repo root.'
    );
    process.exit(1);
  }

  for (const name of uploads) {
    const body = fs.readFileSync(path.join(dir, name));
    process.stdout.write(`  uploading ${name} (${(body.length / 1024 / 1024).toFixed(1)} MB)… `);
    await putObject(name, body, contentTypeFor(name), cacheControlFor(name));
    console.log('done');
  }

  // Confirm the public URL actually serves what we just wrote.
  for (const name of feeds) {
    const url = `${publicUrl}${encodeURIComponent(name)}`;
    try {
      const response = await fetch(url, { method: 'HEAD', cache: 'no-store' });
      console.log(`  verify ${url} → ${response.status}`);
    } catch (err) {
      console.warn(`  verify ${url} failed: ${err.message}`);
    }
  }

  console.log('Upload complete.');
};

main().catch((err) => {
  console.error(err.message ?? err);
  process.exit(1);
});
