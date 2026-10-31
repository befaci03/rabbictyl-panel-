#!/usr/bin/env node
import { build } from "esbuild";
import * as sass from "sass";
import { cp, mkdir, readdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(here, "..");
const repoRoot = path.resolve(frontendRoot, "..");
const webRoot = path.join(repoRoot, "src", "Rabbictyl.Web");
const wwwroot = path.join(webRoot, "wwwroot");
const themesRoot = path.join(repoRoot, "themes");
const addonsRoot = path.join(repoRoot, "addons");

const production =
  process.argv.includes("--production") || process.env.NODE_ENV === "production";
const cleanOnly = process.argv.includes("--clean");

const outputs = {
  assetsCss: path.join(wwwroot, "assets", "css"),
  assetsJs: path.join(wwwroot, "assets", "js"),
  themes: path.join(wwwroot, "themes"),
  addons: path.join(wwwroot, "addons"),
};

const sassStyle = production ? "compressed" : "expanded";

async function clean() {
  await Promise.all(
    Object.values(outputs).map((directory) =>
      rm(directory, { recursive: true, force: true }),
    ),
  );
}

async function assertFile(file, description) {
  try {
    const info = await stat(file);

    if (!info.isFile()) {
      throw new Error("not a file");
    }
  } catch {
    throw new Error(`Missing ${description}: ${path.relative(repoRoot, file)}`);
  }
}

async function compileStyle(entry, outFile, loadPaths) {
  const extension = path.extname(entry).toLowerCase();

  await mkdir(path.dirname(outFile), { recursive: true });

  if (extension === ".css") {
    await cp(entry, outFile);
    return;
  }

  if (extension !== ".scss" && extension !== ".sass") {
    throw new Error(`Unsupported stylesheet '${path.relative(repoRoot, entry)}'.`);
  }

  const result = sass.compile(entry, {
    style: sassStyle,
    loadPaths,
    quietDeps: true,
  });

  await writeFile(outFile, result.css);
}

async function bundle(entry, outFile) {
  await mkdir(path.dirname(outFile), { recursive: true });

  await build({
    entryPoints: [entry],
    outfile: outFile,
    bundle: true,
    format: "esm",
    target: "es2022",
    minify: production,
    sourcemap: production ? false : "linked",
    legalComments: "none",
    logLevel: "warning",
  });
}

async function compileApp() {
  await compileStyle(
    path.join(frontendRoot, "src", "scss", "app.scss"),
    path.join(outputs.assetsCss, "app.css"),
    [path.join(frontendRoot, "src", "scss"), path.join(frontendRoot, "node_modules")],
  );

  await bundle(
    path.join(frontendRoot, "src", "ts", "app.ts"),
    path.join(outputs.assetsJs, "app.js"),
  );
}

async function readExtensions(root) {
  try {
    return (await readdir(root, { withFileTypes: true }))
      .filter((entry) => entry.isDirectory() && !entry.name.startsWith("."))
      .sort((left, right) => left.name.localeCompare(right.name));
  } catch {
    return [];
  }
}

async function compileExtensions(root, kind, outRoot) {
  for (const entry of await readExtensions(root)) {
    const directory = path.join(root, entry.name);
    const manifestPath = path.join(directory, "manifest.json");
    const manifest = JSON.parse(await readFile(manifestPath, "utf8"));

    if (manifest.id !== entry.name) {
      throw new Error(
        `${kind} '${entry.name}': manifest id '${manifest.id}' must match the folder name.`,
      );
    }

    const outDirectory = path.join(outRoot, entry.name);
    const styles = manifest.styles ?? [];
    const scripts = manifest.scripts ?? [];

    if (kind === "addon" && styles.length > 0) {
      throw new Error(`addon '${entry.name}': addons cannot link stylesheets.`);
    }

    for (const style of styles) {
      const extension = path.extname(style).toLowerCase();

      if (extension !== ".css" && extension !== ".scss" && extension !== ".sass") {
        throw new Error(
          `${kind} '${entry.name}': '${style}' must be .css, .scss or .sass.`,
        );
      }

      const source = path.join(directory, style);
      await assertFile(source, `${kind} '${entry.name}' stylesheet`);

      const relative = style.replace(/\.(scss|sass|css)$/i, ".css");
      await compileStyle(
        source,
        path.join(outDirectory, relative),
        [directory, path.join(directory, "styles")],
      );
    }

    for (const script of scripts) {
      if (path.extname(script).toLowerCase() !== ".ts") {
        throw new Error(
          `${kind} '${entry.name}': '${script}' must be TypeScript (.ts). JavaScript is not supported.`,
        );
      }

      const source = path.join(directory, script);
      await assertFile(source, `${kind} '${entry.name}' script`);

      await bundle(
        source,
        path.join(outDirectory, script.replace(/\.ts$/i, ".js")),
      );
    }

    for (const [assetId, relativePath] of Object.entries(manifest.assets ?? {})) {
      if (!/^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$/.test(assetId)) {
        throw new Error(`${kind} '${entry.name}': invalid asset id '${assetId}'.`);
      }

      await assertFile(
        path.join(directory, relativePath),
        `${kind} '${entry.name}' asset '${assetId}'`,
      );
    }

    console.log(`  ${kind} ${entry.name}: ${styles.length} style(s), ${scripts.length} script(s)`);
  }
}

await clean();

if (cleanOnly) {
  console.log("Cleaned generated frontend output.");
  process.exit(0);
}

console.log(`Building frontend${production ? " (production)" : ""}…`);
await compileApp();
await compileExtensions(themesRoot, "theme", outputs.themes);
await compileExtensions(addonsRoot, "addon", outputs.addons);
console.log("Frontend build complete.");
