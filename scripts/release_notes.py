import re
import sys
from pathlib import Path

tag = sys.argv[1].removeprefix('v')
if not re.fullmatch(r'\d+\.\d+\.\d{2}', tag):
    raise SystemExit('Release tags must use major.minor.XX')
notes = Path(__file__).resolve().parent.parent / 'releases' / f'{tag}.md'
body = notes.read_text(encoding='utf-8')
for section in ('## Features', '## Bug Fixes', '## Validation'):
    if section not in body:
        raise SystemExit(f'Missing release section: {section}')
Path(sys.argv[2]).write_text(body, encoding='utf-8')
