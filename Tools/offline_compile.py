#!/usr/bin/env python3
"""Compile the game's scripts outside the Editor, with staged changes swapped in, to
check they build without writing a .cs under Assets/ (which recompiles the shared Editor
under whoever is in play mode). Uses the Editor's own compiler and the response files
of its last build (Library/Bee/artifacts/*E.dag/Assembly-CSharp*.rsp), so references and
defines match; Assets/ is never touched. Run from the repo root:

  python3 Tools/offline_compile.py \
      --use Assets/StarForge/Scripts/View/UnitWreck.cs=Art/Source/staged/UnitWreck.cs \
      --add Assets/StarForge/Scripts/View/NewThing.cs=Art/Source/staged/NewThing.cs \
      --patch Art/Source/staged/patch_something.py

--use replaces a file, --add adds a new one (same syntax), --patch runs a script that
edits files by their repo-relative paths (it runs in a scratch copy of those files).
Prints each assembly's errors; exit status 1 if either fails. Analyzers are skipped, so
warnings differ from the Editor's; errors do not.
"""
import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile

# The newest installed Editor's SDK (the project follows the Editor through upgrades).
_EDITORS = sorted(glob.glob('/Applications/Unity/Hub/Editor/*/Unity.app/Contents/Resources/Scripting/DotNetSdk'))
UNITY = _EDITORS[-1] if _EDITORS else '/Applications/Unity/Hub/Editor/6000.6.5f1/Unity.app/Contents/Resources/Scripting/DotNetSdk'
DOTNET = os.path.join(UNITY, 'dotnet')


def csc():
    found = sorted(glob.glob(os.path.join(UNITY, 'sdk/*/Roslyn/bincore/csc.dll')))
    if not found:
        sys.exit('no csc.dll under ' + UNITY)
    return found[-1]


def rsp_dir():
    dags = [d for d in glob.glob('Library/Bee/artifacts/*E.dag') if os.path.exists(os.path.join(d, 'Assembly-CSharp.rsp'))]
    if not dags:
        sys.exit('no Library/Bee/artifacts/*E.dag/Assembly-CSharp.rsp: let the Editor compile once first')
    return max(dags, key=lambda d: os.path.getmtime(os.path.join(d, 'Assembly-CSharp.rsp')))


def pairs(items):
    out = {}
    for it in items or []:
        dst, src = it.split('=', 1)
        out[dst] = src
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--use', action='append', help='Assets/...cs=staged/file.cs (replace)')
    ap.add_argument('--add', action='append', help='Assets/...cs=staged/file.cs (new file)')
    ap.add_argument('--patch', action='append', help='a script editing repo-relative paths')
    ap.add_argument('--keep', help='scratch directory to keep (default: a temp dir)')
    a = ap.parse_args()

    root = os.getcwd()
    scratch = a.keep or tempfile.mkdtemp(prefix='sf_compile_')
    tree = os.path.join(scratch, 'tree')
    if os.path.exists(tree):
        shutil.rmtree(tree)
    use, add = pairs(a.use), pairs(a.add)
    # Patch scripts edit files under Assets by relative path: give them copies of every
    # script so whatever they touch is the scratch copy.
    if a.patch:
        for f in glob.glob('Assets/**/*.cs', recursive=True) + glob.glob('Assets/**/*.shader', recursive=True):
            os.makedirs(os.path.dirname(os.path.join(tree, f)), exist_ok=True)
            shutil.copy(f, os.path.join(tree, f))
    for dst, src in {**use, **add}.items():
        os.makedirs(os.path.dirname(os.path.join(tree, dst)), exist_ok=True)
        shutil.copy(os.path.join(root, src), os.path.join(tree, dst))
    for script in a.patch or []:
        subprocess.run([sys.executable, os.path.join(root, script)], cwd=tree, check=True)

    dag = rsp_dir()
    compiler = csc()
    failed = False
    built = None
    for name in ['Assembly-CSharp', 'Assembly-CSharp-Editor']:
        rsp = open(os.path.join(dag, name + '.rsp')).read()
        out = os.path.join(scratch, name + '.dll')
        rsp = re.sub(r'-out:"[^"]*"', '-out:"%s"' % out, rsp)
        rsp = re.sub(r'-refout:"[^"]*"\n', '', rsp)
        rsp = re.sub(r'-analyzer:"[^"]*"\n', '', rsp)
        rsp = re.sub(r'[-/]additionalfile:"[^"]*"\n', '', rsp)
        if built:
            rsp = re.sub(r'-r:"[^"]*/Assembly-CSharp(\.ref)?\.dll"', '-r:"%s"' % built, rsp)
        lines = rsp.split('\n')
        srcs = [l.strip('"') for l in lines if l.startswith('"') and l.endswith('.cs"')]
        rest = [l for l in lines if not (l.startswith('"') and l.endswith('.cs"'))]
        editor = name.endswith('Editor')
        for dst in add:
            if ('/Editor/' in dst) == editor and dst not in srcs:
                srcs.append(dst)
        files = [os.path.join(tree, f) if os.path.exists(os.path.join(tree, f)) else os.path.join(root, f) for f in srcs]
        path = os.path.join(scratch, name + '.rsp')
        with open(path, 'w') as fh:
            fh.write('\n'.join(rest) + '\n' + '\n'.join('"%s"' % f for f in files) + '\n')
        r = subprocess.run([DOTNET, compiler, '/noconfig', '@' + path], cwd=root, capture_output=True, text=True)
        errs = [l for l in r.stdout.splitlines() if ': error ' in l]
        print('%s: %s, %d error(s)' % (name, 'ok' if r.returncode == 0 else 'FAILED', len(errs)))
        for e in errs[:40]:
            print('  ' + e.replace(tree + '/', ''))
        if r.returncode != 0:
            failed = True
            break
        built = out
    if not a.keep:
        shutil.rmtree(scratch, ignore_errors=True)
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
