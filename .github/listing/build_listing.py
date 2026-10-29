"""
Builds the VCC (VRChat Creator Companion) listing for this repo from its GitHub releases: index.json with every
released package version, plus a small page with an "Add to VCC" button. Every release carries the package zip,
with package.json at the root of the zip (see .github/workflows/release.yml).

    python build_listing.py <owner/repo> <output folder>

Listing name, id and author come from listing.json next to this script. The listing's URL is the repo's GitHub
Pages address: https://<owner>.github.io/<repo>/index.json
"""
import hashlib
import html
import io
import json
import os
import sys
import urllib.parse
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))


def github(path):
    request = urllib.request.Request(
        "https://api.github.com" + path,
        headers={"Accept": "application/vnd.github+json", "Authorization": "Bearer " + os.environ["GH_TOKEN"]},
    )
    with urllib.request.urlopen(request) as response:
        return json.load(response)


def releases(repo):
    page = 1
    while True:
        batch = github(f"/repos/{repo}/releases?per_page=100&page={page}")
        if not batch:
            return
        yield from batch
        page += 1


def version_entry(zip_bytes, url):
    """The package.json inside a release zip, plus where VCC downloads it from and its checksum."""
    with zipfile.ZipFile(io.BytesIO(zip_bytes)) as archive:
        name = next(n for n in archive.namelist() if (n[2:] if n.startswith("./") else n) == "package.json")
        manifest = json.loads(archive.read(name))
    manifest["url"] = url
    manifest["zipSHA256"] = hashlib.sha256(zip_bytes).hexdigest()
    return manifest


def collect(repo):
    packages = {}
    for release in releases(repo):
        if release["draft"]:
            continue
        for asset in release["assets"]:
            if not asset["name"].endswith(".zip"):
                continue
            # The public download URL needs no token (GitHub's storage redirect rejects one anyway).
            with urllib.request.urlopen(asset["browser_download_url"]) as response:
                entry = version_entry(response.read(), asset["browser_download_url"])
            packages.setdefault(entry["name"], {"versions": {}})["versions"][entry["version"]] = entry
    return packages


def version_key(version):
    # 1.10.0 sorts after 1.9.0; a pre-release (1.0.0-beta) sorts before its release
    core, _, pre = version.split("+")[0].partition("-")
    return [int(p) if p.isdigit() else 0 for p in core.split(".")], pre == "", pre


def page(info, url, packages):
    add = "vcc://vpm/addRepo?url=" + urllib.parse.quote(url, safe="")
    rows = []
    for name, package in sorted(packages.items()):
        latest = package["versions"][max(package["versions"], key=version_key)]
        rows.append(
            f"<li><strong>{html.escape(latest.get('displayName', name))}</strong> {html.escape(latest['version'])}"
            f"<br><span class=muted>{html.escape(latest.get('description', ''))}</span></li>"
        )
    title = html.escape(info["name"])
    return f"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} VCC Listing</title>
<style>
  :root {{ --bg: #faf9f7; --fg: #1d1c1a; --muted: #6b6862; --card: #ffffff; --line: #e4e1dc; --accent: #2b4fa8; }}
  @media (prefers-color-scheme: dark) {{
    :root {{ --bg: #17171a; --fg: #ececee; --muted: #a2a1a8; --card: #212126; --line: #33333a; --accent: #8eb0ff; }}
  }}
  body {{ margin: 0; background: var(--bg); color: var(--fg); font: 16px/1.5 system-ui, sans-serif; }}
  main {{ max-width: 640px; margin: 0 auto; padding: 48px 16px; }}
  h1 {{ margin: 0 0 4px; font-size: 28px; }}
  .muted {{ color: var(--muted); }}
  .card {{ background: var(--card); border: 1px solid var(--line); border-radius: 10px; padding: 20px; margin-top: 24px; }}
  .button {{ display: inline-block; background: var(--accent); color: var(--bg); padding: 10px 18px; border-radius: 8px;
             text-decoration: none; font-weight: 600; }}
  code {{ display: block; margin-top: 8px; padding: 10px; border-radius: 6px; background: var(--bg);
          border: 1px solid var(--line); overflow-wrap: anywhere; font-size: 14px; }}
  ol, ul {{ padding-left: 20px; }} li {{ margin: 6px 0; }}
</style>
</head>
<body>
<main>
  <h1>{title}</h1>
  <p class="muted">{html.escape(info.get("description", ""))}</p>
  <div class="card">
    <a class="button" href="{html.escape(add)}">Add to VCC</a>
    <p>Or in the Creator Companion: <em>Settings → Packages → Add Repository</em>, and paste:</p>
    <code>{html.escape(url)}</code>
    <ol>
      <li>In VCC, open your avatar project with <em>Manage Project</em>.</li>
      <li>Find the package below in the list and click <strong>+</strong> to add it.</li>
    </ol>
  </div>
  <div class="card">
    <strong>Packages</strong>
    <ul>{"".join(rows) or "<li class=muted>Nothing released yet.</li>"}</ul>
  </div>
</main>
</body>
</html>
"""


def write(out, info, url, packages):
    listing = {"name": info["name"], "id": info["id"], "url": url, "author": info["author"], "packages": packages}
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "index.json"), "w", encoding="utf-8") as f:
        json.dump(listing, f, indent=2, ensure_ascii=False)
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8") as f:
        f.write(page(info, url, packages))


def main(repo, out):
    owner, name = repo.split("/")
    with open(os.path.join(HERE, "listing.json"), encoding="utf-8") as f:
        info = json.load(f)
    url = f"https://{owner.lower()}.github.io/{name}/index.json"
    packages = collect(repo)
    write(out, info, url, packages)
    for package, data in packages.items():
        print(f"{package}: {', '.join(sorted(data['versions'], key=version_key))}")
    print(f"Listing: {url}")


if __name__ == "__main__":
    main(*sys.argv[1:3])
