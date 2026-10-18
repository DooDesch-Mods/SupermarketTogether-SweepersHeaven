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


def canonical_embeds(embeds):
    result = []
    for embed in embeds:
        row = {key: embed[key] for key in ("title", "url", "description", "color") if key in embed}
        if "fields" in embed:
            row["fields"] = [{"name": field["name"], "value": field["value"], "inline": field.get("inline", False)} for field in embed["fields"]]
        for key, value in (("thumbnail", "url"), ("footer", "text")):
            if key in embed:
                row[key] = {value: embed[key][value]}
        result.append(row)
    return result


def discord(url, method, repo, tag, payload=None):
    request = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8") if payload is not None else None,
        headers={"Content-Type": "application/json", "User-Agent": "DooDesch-release-announcement"}, method=method)
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"Discord {method} failed for {repo} {tag}: HTTP {error.code}; no automatic retry.") from None
    except (urllib.error.URLError, TimeoutError):
        raise RuntimeError(f"Discord {method} acknowledgement unavailable for {repo} {tag}; check the channel before retrying.") from None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tag")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--output", default="discord-announcement.json")
    parser.add_argument("--message-id", help="Update this existing release announcement instead of sending a new message.")
    args = parser.parse_args()
    if args.message_id and not re.fullmatch(r"[1-9]\d{16,19}", args.message_id):
        raise RuntimeError("Invalid Discord message ID; no message sent or edited.")
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
    notes = "\n".join(release["body"].splitlines()).strip()
    if not notes:
        raise RuntimeError("The public release has no announcement notes.")
    notes = re.sub(r"^#{1,6}\s+(.+)$", r"**\1**", notes, flags=re.M)
    payload = {"allowed_mentions": {"parse": []}, "embeds": [{
        "title": f"{name} {version}", "url": release["url"],
        "description": notes if len(notes) <= 1400 else notes[:1396].rsplit(" ", 1)[0] + "\n...",
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
    if args.message_id:
        parts = parts._replace(path=parts.path.rstrip("/") + f"/messages/{args.message_id}")
        url = urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(query)))
        previous = discord(url, "GET", repo, args.tag)
        embeds = previous.get("embeds", [])
        if len(embeds) != 1 or embeds[0].get("title") != f"{name} {version}" or embeds[0].get("url") != release["url"]:
            raise RuntimeError("The existing message belongs to a different release; no edit performed.")
        message = discord(url, "PATCH", repo, args.tag, payload)
    else:
        url = urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(query)))
        message = discord(url, "POST", repo, args.tag, payload)
    if not message.get("id") or not message.get("channel_id"):
        raise RuntimeError("Discord returned no message acknowledgement; check the channel before retrying.")
    if args.message_id and message["id"] != args.message_id:
        raise RuntimeError("Discord acknowledged a different message; inspect the channel before retrying.")
    read_parts = urllib.parse.urlsplit(url)
    if not args.message_id:
        read_parts = read_parts._replace(path=read_parts.path.rstrip("/") + f"/messages/{message['id']}")
    confirmed = discord(urllib.parse.urlunsplit(read_parts), "GET", repo, args.tag)
    if canonical_embeds(confirmed.get("embeds", [])) != canonical_embeds(payload["embeds"]):
        raise RuntimeError("Discord message readback differs from the intended embed; inspect before retrying.")
    receipt = {"repository": repo, "tag": args.tag, "messageId": message["id"], "channelId": message["channel_id"],
        "title": message["embeds"][0]["title"], "status": "confirmed", "operation": "updated" if args.message_id else "created",
        "embedReadbackMatches": True, "embed": canonical_embeds(confirmed["embeds"])[0]}
    output.write_text(json.dumps(receipt, indent=2)+"\n", encoding="utf-8")
    print(f"info: Discord confirmed {receipt['title']}; message {receipt['messageId']} in channel {receipt['channelId']}.")
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
            summary.write(f"Discord confirmed **{receipt['title']}**. Message ID: `{receipt['messageId']}`.\n")


if __name__ == "__main__":
    main()
