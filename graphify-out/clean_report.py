import re

path = "docs/audits/FSCM_Backend_GraphQL_Entity_Capability_Audit.md"
with open(path, "r", encoding="utf-8") as f:
    text = f.read()

replacements = [
    ("â€“", "–"),
    ("â€”", "—"),
    ("â€œ", "\""),
    ("â€\x9d", "\""),
    ("â€˜", "'"),
    ("â€™", "'"),
    ("Â·", "·"),
    ("â€¢", "•"),
    ("âœ“", "✓"),
    ("âœ—", "✗"),
    ("â†’", "→"),
    ("â‰¥", "≥"),
    ("â‰¤", "≤"),
    ("â‰", "≠")
]

for old, new in replacements:
    text = text.replace(old, new)

# Also fix any remaining â€” or â€“
text = re.sub(r'â€“', '–', text)
text = re.sub(r'â€”', '—', text)

with open(path, "w", encoding="utf-8") as f:
    f.write(text)

print(f"Cleaned {path}, length: {len(text)}")
