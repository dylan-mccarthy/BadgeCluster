#!/usr/bin/env python3
"""Load nonsecret deployment identifiers without requiring GitHub admin APIs."""
import json
import os
from pathlib import Path
import re
import sys

KEYS = {'AZURE_CLIENT_ID', 'AZURE_TENANT_ID', 'AZURE_SUBSCRIPTION_ID', 'AZURE_RESOURCE_GROUP'}
UUID = re.compile(r'[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}')


def main():
    path = Path(sys.argv[1] if len(sys.argv) > 1 else 'infra/deployment.json')
    settings = json.loads(path.read_text())
    if not isinstance(settings, dict) or set(settings) != KEYS:
        raise ValueError('Expected exactly the four nonsecret Azure deployment identifiers')
    for key, value in settings.items():
        if not isinstance(value, str):
            raise ValueError('Invalid identifier: ' + key)
        pattern = r'[a-zA-Z0-9_-]{1,90}' if key == 'AZURE_RESOURCE_GROUP' else UUID
        if not re.fullmatch(pattern, value):
            raise ValueError('Invalid identifier: ' + key)
    # Validate every value before writing either GitHub runner output file.
    environment = ''.join(f'{key}={settings[key]}\n' for key in sorted(KEYS))
    outputs = ''.join(f'{key.lower()}={settings[key]}\n' for key in sorted(KEYS))
    with Path(os.environ['GITHUB_ENV']).open('a') as target:
        target.write(environment)
    with Path(os.environ['GITHUB_OUTPUT']).open('a') as target:
        target.write(outputs)
    print('Loaded nonsecret Azure deployment configuration.')


if __name__ == '__main__':
    try:
        main()
    except (OSError, KeyError, ValueError) as error:
        print('Deployment configuration error: ' + str(error), file=sys.stderr)
        sys.exit(1)
