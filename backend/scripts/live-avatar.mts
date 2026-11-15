import { readFile, writeFile } from "node:fs/promises";
import { basename } from "node:path";

const site = process.env.CONVEX_SITE_URL;
const photos = process.argv.slice(2);
if (!site || photos.length === 0) {
  console.error("usage: CONVEX_SITE_URL=https://<deployment>.convex.site npx tsx scripts/live-avatar.mts photo.jpg [more.jpg …]");
  process.exit(1);
}

const results = [];
for (const path of photos) {
  const name = basename(path).replace(/\.[^.]+$/, "");
  const playerId = `live-${name}`.replace(/[^A-Za-z0-9_\-:.]/g, "-").slice(0, 64);
  const startedAt = Date.now();
  const response = await fetch(`${site}/api/avatar/photo?playerId=${playerId}`, {
    method: "POST",
    headers: { "Content-Type": "image/jpeg" },
    body: await readFile(path),
  });
  const body = await response.json();
  const roundTripMs = Date.now() - startedAt;
  results.push({ photo: basename(path), status: response.status, roundTripMs, ...body });
  const { avatar, decision, error } = body;
  const look = avatar?.costume ? `costume ${avatar.costume} (${decision.costumeProbability})` : `outfit ${avatar?.outfit}`;
  console.log(
    response.ok
      ? `${name}: ${decision.provider} ${decision.latencyMs} ms${decision.hedged ? " (hedged)" : ""}, person ${decision.realPerson}, ${look} → ${avatar.base} ${avatar.figure ?? "-"}/${avatar.age ?? "-"}/${avatar.build ?? "-"}/${avatar.expression ?? "-"} ${JSON.stringify(avatar.swatches)}`
      : `${name}: HTTP ${response.status} ${error.code}${error.realPerson !== undefined ? ` (person ${error.realPerson})` : ""}: ${error.message}`,
  );
}
if (process.env.OUT) await writeFile(process.env.OUT, JSON.stringify(results, null, 2));
