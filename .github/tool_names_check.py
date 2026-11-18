#!/usr/bin/env python3
"""No AI tool's name in the repository (docs/development.md#no-tool-names). CI's Changelog job runs it on every pull
request; run it locally with `python3 .github/tool_names_check.py`.

Anything can run Requestarr's automation, so the code, docs, scripts, data and file metadata say "automation" or
"automated checks", never the tool that happened to run them. Checked: every file git tracks, as bytes (text, and any
metadata a binary file carries in plain text, such as an image's), and the compressed text and other metadata inside
PNG images. The names are built from parts below, so this file doesn't name them itself.
"""
import os
import re
import struct
import subprocess
import sys
import tempfile
import zlib

NAMES = ["cl" + "aude", "anthro" + "pic"]
FOUND = re.compile("|".join(NAMES).encode(), re.IGNORECASE)
PNG = b"\x89PNG\r\n\x1a\n"
COMPRESSED_TEXT = {b"zTXt", b"iTXt"}


def png_texts(data):
    """The decompressed text of a PNG's compressed text chunks: plain bytes don't show what they say."""
    texts, i = [], len(PNG)
    while i + 8 <= len(data):
        length, kind = struct.unpack(">I4s", data[i:i + 8])
        body = data[i + 8:i + 8 + length]
        i += 12 + length
        if kind in COMPRESSED_TEXT:
            texts.append(compressed_text(kind, body))
    return texts


def compressed_text(kind, body):
    _, rest = body.split(b"\0", 1)
    if kind == b"zTXt":
        return safe_inflate(rest[1:])
    flag, rest = rest[0], rest[2:]
    _, _, text = rest.split(b"\0", 2)
    return safe_inflate(text) if flag else text


def safe_inflate(data):
    try:
        return zlib.decompress(data)
    except zlib.error:
        return b""


def problems(path):
    """Where a file names a tool: "line 12" in text, "metadata" in an image's compressed text."""
    data = open(path, "rb").read()
    found = []
    if FOUND.search(data):
        lines = data.split(b"\n")
        found = ["line %d" % n for n, line in enumerate(lines, 1) if FOUND.search(line)] if b"\0" not in data else ["its bytes"]
    if data.startswith(PNG) and any(FOUND.search(t) for t in png_texts(data)):
        found.append("image metadata")
    return found


def tracked(root):
    out = subprocess.run(["git", "-C", root, "ls-files", "-z"], capture_output=True, check=True).stdout
    return [p.decode() for p in out.split(b"\0") if p and os.path.isfile(os.path.join(root, p.decode()))]


def check(root):
    failures = []
    for path in tracked(root):
        failures += ["%s (%s): names an AI tool; say \"automation\" or \"automated checks\"" % (path, where) for where in problems(os.path.join(root, path))]
    return failures


def png(chunks):
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    header = chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 0, 0, 0, 0))
    return PNG + header + b"".join(chunk(k, b) for k, b in chunks) + chunk(b"IDAT", zlib.compress(b"\0\0")) + chunk(b"IEND", b"")


def self_test():
    name = NAMES[0].title()
    samples = {
        "clean.md": (b"Checked automatically: the live check.\n", []),
        "text.md": (b"First line.\nWritten by %s.\n" % name.encode(), ["line 2"]),
        "company.py": (b"# %s\n" % NAMES[1].upper().encode(), ["line 1"]),
        "plain-metadata.png": (png([(b"tEXt", b"Software\0" + name.encode())]), ["its bytes"]),
        "hidden-metadata.png": (png([(b"zTXt", b"Comment\0\0" + zlib.compress(b"made with " + name.encode()))]), ["image metadata"]),
        "international-metadata.png": (png([(b"iTXt", b"Comment\0\1\0\0\0" + zlib.compress(name.encode()))]), ["image metadata"]),
        "clean.png": (png([(b"gAMA", struct.pack(">I", 45455))]), []),
    }
    failed = []
    with tempfile.TemporaryDirectory() as root:
        for file, (data, _) in samples.items():
            with open(os.path.join(root, file), "wb") as f:
                f.write(data)
        for file, (_, expected) in samples.items():
            got = problems(os.path.join(root, file))
            if got != expected:
                failed.append("%s: expected %s, got %s" % (file, expected, got))
        subprocess.run(["git", "-C", root, "init", "-q"], check=True)
        subprocess.run(["git", "-C", root, "-c", "core.autocrlf=false", "add", "."], check=True)
        if len(check(root)) != 5:
            failed.append("check over a repository: expected 5 findings, got %s" % check(root))
    for line in failed:
        print("SELF-TEST FAILED: " + line)
    if not failed:
        print("Tool names self-test passed: %d samples." % len(samples))
    return not failed


def main(args):
    if args == ["--self-test"]:
        return 0 if self_test() else 1
    root = args[0] if args else os.getcwd()
    failures = check(root)
    for line in failures:
        print(line)
    print("No AI tool named in the %d tracked files." % len(tracked(root)) if not failures else "%d places name an AI tool." % len(failures))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
