"""Announce an existing public release through the organization's Discord webhook."""
import argparse
import base64
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request


def github(*args):
    result = subprocess.run(["gh", *args], capture_output=True, text=True, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"GitHub read failed: {result.stderr.strip()}")
    return json.loads(result.stdout)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tag")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--output", default="discord-announcement.json")
    args = parser.parse_args()
    if not re.fullmatch(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", args.tag):
        raise RuntimeError("Expected a release tag vX.Y.Z.")
    repo = os.environ["GITHUB_REPOSITORY"]
    release = github("release", "view", args.tag, "--repo", repo, "--json", "tagName,isDraft,isPrerelease,body,url")
    if release["isDraft"] or release["isPrerelease"] or release["tagName"] != args.tag:
        raise RuntimeError("Discord announcements require an existing public stable release.")
    metadata = github("api", f"repos/{repo}/contents/thunderstore/manifest.json?ref={args.tag}")
    manifest = json.loads(base64.b64decode(metadata["content"]))
    name, version = manifest["name"], args.tag[1:]
    if manifest["version_number"] != version:
        raise RuntimeError("The released manifest and tag versions differ.")
    notes = release["body"].strip()
    if not notes:
        raise RuntimeError("The public release has no announcement notes.")
    payload = {"allowed_mentions": {"parse": []}, "embeds": [{
        "title": f"{name} {version}", "url": release["url"],
        "description": notes if len(notes) <= 1400 else notes[:1396] + "\n...",
        "color": 8256434,
        "thumbnail": {"url": f"https://raw.githubusercontent.com/{repo}/{args.tag}/thunderstore/icon.png"},
        "fields": [{"name": "Get it", "value": f"[Thunderstore](https://thunderstore.io/c/supermarket-together/p/DooDesch/{name}/) - [GitHub]({release['url']})"}],
        "footer": {"text": f"Supermarket Together - {repo.split('/')[-1]}"}
    }]}
    output = Path(args.output)
    if args.dry_run:
        output.write_text(json.dumps(payload, indent=2, ensure_ascii=False)+"\n", encoding="utf-8")
        print(f"info: Prepared Discord embed for {name} {version}; no message sent.")
        return
    webhook = os.environ.get("DISCORD_RELEASE_WEBHOOK", "")
    if not webhook:
        raise RuntimeError("DISCORD_RELEASE_WEBHOOK is missing; no announcement sent.")
    parts = urllib.parse.urlsplit(webhook)
    query = dict(urllib.parse.parse_qsl(parts.query))
    query["wait"] = "true"
    url = urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(query)))
    request = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json", "User-Agent": "DooDesch-release-announcement"}, method="POST")
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            message = json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"Discord rejected {repo} {args.tag}: HTTP {error.code}; no automatic retry.") from None
    except (urllib.error.URLError, TimeoutError):
        raise RuntimeError(f"Discord acknowledgement unavailable for {repo} {args.tag}; delivery is uncertain. Check the channel before retrying.") from None
    if not message.get("id") or not message.get("channel_id"):
        raise RuntimeError("Discord returned no message acknowledgement; check the channel before retrying.")
    receipt = {"repository": repo, "tag": args.tag, "messageId": message["id"], "channelId": message["channel_id"],
        "title": message["embeds"][0]["title"], "status": "confirmed"}
    output.write_text(json.dumps(receipt, indent=2)+"\n", encoding="utf-8")
    print(f"info: Discord confirmed {receipt['title']}; message {receipt['messageId']} in channel {receipt['channelId']}.")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(f"Discord confirmed **{receipt['title']}**. Message ID: `{receipt['messageId']}`.\n")


if __name__ == "__main__":
    main()
