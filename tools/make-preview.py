"""Renders README.md to a GitHub-style HTML file for local preview.

Run: python tools/make-preview.py
"""

import base64
import html
import io
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "README.md")
TARGET = os.path.join(ROOT, "README-preview.html")

STYLE = """
:root { color-scheme: light; }
* { box-sizing: border-box; }
body {
  margin: 0; padding: 32px 16px 80px;
  background: #ffffff; color: #1f2328;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Malgun Gothic", Helvetica, Arial, sans-serif;
  font-size: 16px; line-height: 1.6;
}
.notice {
  max-width: 860px; margin: 0 auto 24px; padding: 10px 14px;
  background: #fff8c5; border: 1px solid #d4a72c66; border-radius: 6px;
  font-size: 13px; color: #57606a;
}
main { max-width: 860px; margin: 0 auto; }
h1, h2, h3 { font-weight: 600; line-height: 1.25; margin: 28px 0 12px; }
h1 { font-size: 2em; border-bottom: 1px solid #d8dee4; padding-bottom: .3em; }
h2 { font-size: 1.5em; border-bottom: 1px solid #d8dee4; padding-bottom: .3em; }
h3 { font-size: 1.2em; }
p { margin: 0 0 14px; }
ul, ol { margin: 0 0 14px; padding-left: 28px; }
li { margin: 4px 0; }
a { color: #0969da; text-decoration: none; }
a:hover { text-decoration: underline; }
code {
  font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace;
  font-size: 85%; background: #f6f8fa; padding: .2em .4em; border-radius: 6px;
}
pre {
  background: #f6f8fa; padding: 16px; border-radius: 6px; overflow: auto;
  margin: 0 0 16px;
}
pre code { background: none; padding: 0; font-size: 85%; }
table { border-collapse: collapse; margin: 0 0 16px; display: block; overflow: auto; }
th, td { border: 1px solid #d0d7de; padding: 6px 13px; }
th { background: #f6f8fa; font-weight: 600; }
blockquote {
  margin: 0 0 16px; padding: 0 1em; color: #57606a;
  border-left: .25em solid #d0d7de;
}
blockquote p { margin: 8px 0; }
details { margin: 0 0 16px; }
summary { cursor: pointer; font-weight: 600; margin-bottom: 8px; }
img { max-width: 100%; }
"""


def inline(text):
    codes = []

    def stash(match):
        codes.append(match.group(1))
        return "\x00%d\x00" % (len(codes) - 1)

    text = re.sub(r"`([^`]+)`", stash, text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)

    def unstash(match):
        return "<code>" + html.escape(codes[int(match.group(1))], quote=False) + "</code>"

    return re.sub("\x00(\\d+)\x00", unstash, text)


def split_row(line):
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def render(markdown_text):
    lines = markdown_text.splitlines()
    out = []
    i = 0

    while i < len(lines):
        line = lines[i]

        if not line.strip():
            i += 1
            continue

        if line.startswith("```"):
            i += 1
            block = []
            while i < len(lines) and not lines[i].startswith("```"):
                block.append(lines[i])
                i += 1
            i += 1
            out.append("<pre><code>" + html.escape("\n".join(block)) + "</code></pre>")
            continue

        if line.startswith("<"):
            out.append(line)
            i += 1
            continue

        match = re.match(r"^(#{1,6})\s+(.*)$", line)
        if match:
            level = len(match.group(1))
            out.append("<h%d>%s</h%d>" % (level, inline(match.group(2)), level))
            i += 1
            continue

        if line.startswith(">"):
            block = []
            while i < len(lines) and lines[i].startswith(">"):
                block.append(re.sub(r"^>\s?", "", lines[i]))
                i += 1
            out.append("<blockquote><p>" + "<br>".join(inline(x) for x in block) + "</p></blockquote>")
            continue

        if re.match(r"^[-*]\s+", line):
            block = []
            while i < len(lines) and re.match(r"^[-*]\s+", lines[i]):
                block.append(re.sub(r"^[-*]\s+", "", lines[i]))
                i += 1
            out.append("<ul>" + "".join("<li>%s</li>" % inline(x) for x in block) + "</ul>")
            continue

        if re.match(r"^\d+\.\s+", line):
            block = []
            while i < len(lines) and re.match(r"^\d+\.\s+", lines[i]):
                block.append(re.sub(r"^\d+\.\s+", "", lines[i]))
                i += 1
            out.append("<ol>" + "".join("<li>%s</li>" % inline(x) for x in block) + "</ol>")
            continue

        if (
            "|" in line
            and i + 1 < len(lines)
            and re.match(r"^\|?[\s:|-]+\|?$", lines[i + 1])
            and "-" in lines[i + 1]
        ):
            header = split_row(line)
            i += 2
            rows = []
            while i < len(lines) and "|" in lines[i] and lines[i].strip():
                rows.append(split_row(lines[i]))
                i += 1
            table = ["<table><thead><tr>"]
            table += ["<th>%s</th>" % inline(cell) for cell in header]
            table.append("</tr></thead><tbody>")
            for row in rows:
                table.append("<tr>" + "".join("<td>%s</td>" % inline(cell) for cell in row) + "</tr>")
            table.append("</tbody></table>")
            out.append("".join(table))
            continue

        block = []
        while i < len(lines) and lines[i].strip() and not re.match(r"^(#{1,6}\s|>|[-*]\s|\d+\.\s|```|<)", lines[i]):
            block.append(lines[i])
            i += 1
        out.append("<p>" + inline(" ".join(block)) + "</p>")

    return "\n".join(out)


def main():
    markdown_text = io.open(SOURCE, encoding="utf-8").read()

    icon_path = os.path.join(ROOT, "docs", "icon.png")
    if os.path.exists(icon_path):
        with open(icon_path, "rb") as handle:
            icon = base64.b64encode(handle.read()).decode("ascii")
        markdown_text = markdown_text.replace(
            'src="docs/icon.png"', 'src="data:image/png;base64,' + icon + '"'
        )

    body = render(markdown_text)

    page = (
        "<!doctype html>\n<html lang=\"ko\">\n<head>\n<meta charset=\"utf-8\">\n"
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n"
        "<title>README 미리보기 — Steam 한글 가상 키보드</title>\n"
        "<style>" + STYLE + "</style>\n</head>\n<body>\n"
        "<div class=\"notice\">로컬 미리보기입니다. GitHub에 올리면 저장소 첫 화면에 같은 내용이 표시됩니다.</div>\n"
        "<main>\n" + body + "\n</main>\n</body>\n</html>\n"
    )

    io.open(TARGET, "w", encoding="utf-8", newline="\n").write(page)
    print("wrote", TARGET)


if __name__ == "__main__":
    main()
