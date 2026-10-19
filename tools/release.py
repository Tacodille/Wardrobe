"""Build Wardrobe, package it for Dalamud's plugin installer, and refresh repo.json.

Usage:  python tools/release.py            (build, zip, update repo.json)
        python tools/release.py --publish  (also commit, push, and attach latest.zip to a GitHub release with gh)

The version comes from <Version> in Wardrobe.csproj. Bump it before releasing so players get the update prompt.
"""
import json
import os
import re
import subprocess
import sys
import time
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OWNER_REPO = 'Tacodille/Wardrobe'


def run(*args, check=True):
    print('>', ' '.join(args))
    return subprocess.run(args, cwd=ROOT, check=check)


def version():
    text = open(os.path.join(ROOT, 'Wardrobe.csproj'), encoding='utf-8').read()
    return re.search(r'<Version>([\d.]+)</Version>', text).group(1)


def main():
    publish = '--publish' in sys.argv
    ver = version()
    parts = ver.split('.')
    while len(parts) < 4:
        parts.append('0')
    assembly_version = '.'.join(parts[:4])

    run('dotnet', 'build', '-c', 'Release')
    out = os.path.join(ROOT, 'bin', 'Release')

    zip_path = os.path.join(ROOT, 'latest.zip')
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
        for name in sorted(os.listdir(out)):
            full = os.path.join(out, name)
            if os.path.isfile(full) and name.lower().endswith(('.dll', '.json')):
                z.write(full, name)  # files at the zip root, which is what the installer expects
    print('wrote', zip_path)

    manifest = json.load(open(os.path.join(ROOT, 'Wardrobe.json'), encoding='utf-8-sig'))
    link = f'https://github.com/{OWNER_REPO}/releases/latest/download/latest.zip'
    entry = {
        'Author': manifest['Author'],
        'Name': manifest['Name'],
        'InternalName': manifest['InternalName'],
        'AssemblyVersion': assembly_version,
        'Punchline': manifest.get('Punchline', ''),
        'Description': manifest.get('Description', ''),
        'ApplicableVersion': manifest.get('ApplicableVersion', 'any'),
        'RepoUrl': manifest.get('RepoUrl', f'https://github.com/{OWNER_REPO}'),
        'Tags': manifest.get('Tags', []),
        'DalamudApiLevel': 15,
        'LoadPriority': 0,
        'IsHide': False,
        'IsTestingExclusive': False,
        'DownloadLinkInstall': link,
        'DownloadLinkTesting': link,
        'DownloadLinkUpdate': link,
        'LastUpdate': int(time.time()),
        'DownloadCount': 0,
    }
    with open(os.path.join(ROOT, 'repo.json'), 'w', encoding='utf-8') as f:
        json.dump([entry], f, indent=2, ensure_ascii=False)
        f.write('\n')
    print('updated repo.json for', assembly_version)

    if publish:
        tag = f'v{ver}'
        run('git', 'add', 'repo.json', 'Wardrobe.csproj')
        run('git', 'commit', '-m', f'Release {tag}', check=False)
        run('git', 'push')
        exists = subprocess.run(['gh', 'release', 'view', tag], cwd=ROOT, capture_output=True).returncode == 0
        if exists:
            run('gh', 'release', 'upload', tag, zip_path, '--clobber')
        else:
            run('gh', 'release', 'create', tag, zip_path, '--title', f'Wardrobe {ver}', '--notes', f'Wardrobe {ver}. Add the custom repository URL from the README to install or update it from the plugin list.')


if __name__ == '__main__':
    main()
