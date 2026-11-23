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

--use replaces a file (it must be one the Editor compiles: a typo is an error, not a
silent pass), --add adds a new one (same syntax), --patch runs a script that edits files
by their repo-relative paths (it runs in a scratch copy of every script; files it creates
are compiled, files it deletes are left out). The source list is the response file's,
corrected for scripts added or deleted on disk since the Editor's last compile (listed
as a note). Prints each assembly's errors; exit status 1 if either fails. Analyzers are
skipped, so warnings differ from the Editor's; errors do not.

  python3 Tools/offline_compile.py --tree HEAD     # the committed tree on its own

--tree REF compiles the scripts of a commit as committed (git archive of Assets/**/*.cs
into the scratch directory: no working-tree file, no peer's uncommitted or untracked
script), with the Editor's references and defines: "HEAD compiles on its own" before a
push. --use/--add/--patch then apply on top of that tree.
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
UNITY = _EDITORS[-1] if _EDITORS else '/Applications/Unity/Hub/Editor/6000.6.10f1/Unity.app/Contents/Resources/Scripting/DotNetSdk'
DOTNET = os.path.join(UNITY, 'dotnet')
NAMES = ['Assembly-CSharp', 'Assembly-CSharp-Editor']


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
        if '=' not in it:
            sys.exit('expected Assets/...cs=staged/file.cs, got ' + it)
        dst, src = it.split('=', 1)
        out[os.path.normpath(dst)] = src
    return out


def is_editor(path):
    return '/Editor/' in '/' + path


def compiled_by_unity(path):
    # Unity skips hidden folders/files and those ending in '~'.
    return not any(p.startswith('.') or p.endswith('~') for p in path.split('/'))


def scripts_in(base):
    return {os.path.relpath(f, base) for f in glob.glob(os.path.join(base, 'Assets/**/*.cs'), recursive=True)
            if compiled_by_unity(os.path.relpath(f, base))}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--use', action='append', help='Assets/...cs=staged/file.cs (replace)')
    ap.add_argument('--add', action='append', help='Assets/...cs=staged/file.cs (new file)')
    ap.add_argument('--patch', action='append', help='a script editing repo-relative paths')
    ap.add_argument('--keep', help='scratch directory to keep (default: a temp dir)')
    ap.add_argument('--tree', metavar='REF', help='compile the scripts of commit REF as committed (e.g. HEAD)')
    a = ap.parse_args()

    root = os.getcwd()
    dag = rsp_dir()
    rsps, srcs = {}, {}
    for name in NAMES:
        rsps[name] = open(os.path.join(dag, name + '.rsp')).read()
        srcs[name] = [l.strip('"') for l in rsps[name].split('\n') if l.startswith('"') and l.endswith('.cs"')]
    known = set(srcs[NAMES[0]]) | set(srcs[NAMES[1]])

    scratch = a.keep or tempfile.mkdtemp(prefix='sf_compile_')
    tree = os.path.join(scratch, 'tree')
    if os.path.exists(tree):
        shutil.rmtree(tree)
    if a.tree:
        # The commit's own scripts, not the working tree's: the source list is the commit's.
        os.makedirs(tree)
        arch = subprocess.run(['git', 'archive', '--format=tar', a.tree, '--', ':(glob)Assets/**/*.cs'],
                              cwd=root, capture_output=True)
        if arch.returncode != 0:
            sys.exit('git archive %s failed: %s' % (a.tree, arch.stderr.decode(errors='replace').strip()))
        subprocess.run(['tar', '-x', '-C', tree], input=arch.stdout, check=True)
        committed = scripts_in(tree)
        print('compiling %s as committed: %d scripts' % (a.tree, len(committed)))
        for name in NAMES:
            ed = name.endswith('Editor')
            srcs[name] = sorted(f for f in committed if is_editor(f) == ed)
        known = committed

    use, add = pairs(a.use), pairs(a.add)
    bad = [d for d in use if d not in known and (a.tree or not os.path.exists(os.path.join(root, d)))]
    if bad:
        sys.exit('--use names a script the Editor does not compile (typo? a new file wants --add): ' + ', '.join(bad))
    for d, s in {**use, **add}.items():
        if not os.path.isfile(os.path.join(root, s)):
            sys.exit('staged file not found: ' + s)

    # The response file is the Editor's last compile: correct it for scripts added or
    # deleted on disk since (a peer's new file, a removed one that would be CS2001).
    disk = scripts_in(root)
    new_on_disk, gone = ([], []) if a.tree else (sorted(disk - known), sorted(known - disk))
    if new_on_disk or gone:
        print('note: the Editor has not compiled the tree as it is on disk; compiling the disk state:')
        for f in new_on_disk:
            print('  + ' + f)
        for f in gone:
            print('  - ' + f)
    if not a.tree:
        for name in NAMES:
            ed = name.endswith('Editor')
            srcs[name] = [f for f in srcs[name] if f in disk or f in use] + [f for f in new_on_disk if is_editor(f) == ed]

    # Patch scripts edit files under Assets by relative path: give them copies of every
    # script so whatever they touch is the scratch copy (a --tree export has them already).
    if a.patch and not a.tree:
        for f in glob.glob('Assets/**/*.cs', recursive=True) + glob.glob('Assets/**/*.shader', recursive=True):
            os.makedirs(os.path.dirname(os.path.join(tree, f)), exist_ok=True)
            shutil.copy(f, os.path.join(tree, f))
    for dst, src in {**use, **add}.items():
        os.makedirs(os.path.dirname(os.path.join(tree, dst)), exist_ok=True)
        shutil.copy(os.path.join(root, src), os.path.join(tree, dst))
    for script in a.patch or []:
        subprocess.run([sys.executable, os.path.join(root, script)], cwd=tree, check=True)
    if a.patch:
        after = scripts_in(tree)
        for name in NAMES:
            ed = name.endswith('Editor')
            srcs[name] = [f for f in srcs[name] if f in after] + \
                         sorted(f for f in after if f not in srcs[name] and f not in known and is_editor(f) == ed)
    for dst in add:
        for name in NAMES:
            if is_editor(dst) == name.endswith('Editor') and dst not in srcs[name]:
                srcs[name].append(dst)

    compiler = csc()
    failed = False
    built = None
    for name in NAMES:
        rsp = rsps[name]
        out = os.path.join(scratch, name + '.dll')
        rsp = re.sub(r'-out:"[^"]*"', '-out:"%s"' % out, rsp)
        rsp = re.sub(r'-refout:"[^"]*"\n', '', rsp)
        rsp = re.sub(r'-analyzer:"[^"]*"\n', '', rsp)
        rsp = re.sub(r'[-/]additionalfile:"[^"]*"\n', '', rsp)
        if built:
            rsp = re.sub(r'-r:"[^"]*/Assembly-CSharp(\.ref)?\.dll"', '-r:"%s"' % built, rsp)
        rest = [l for l in rsp.split('\n') if not (l.startswith('"') and l.endswith('.cs"'))]
        files = [os.path.join(tree, f) if a.tree or os.path.exists(os.path.join(tree, f)) else os.path.join(root, f) for f in srcs[name]]
        path = os.path.join(scratch, name + '.rsp')
        with open(path, 'w') as fh:
            fh.write('\n'.join(rest) + '\n' + '\n'.join('"%s"' % f for f in files) + '\n')
        r = subprocess.run([DOTNET, compiler, '/noconfig', '@' + path], cwd=root, capture_output=True, text=True)
        # 'error CS' also catches diagnostics with no file prefix (CS2001, CS0006).
        errs = [l for l in r.stdout.splitlines() if re.search(r'\berror [A-Z]+\d+', l)]
        print('%s: %s, %d error(s)' % (name, 'ok' if r.returncode == 0 else 'FAILED', len(errs)))
        for e in errs[:40]:
            print('  ' + e.replace(tree + '/', ''))
        if r.returncode != 0:
            if not errs:   # the compiler itself failed: show why
                for l in (r.stdout + r.stderr).strip().splitlines()[-20:]:
                    print('  | ' + l)
            failed = True
            break
        built = out
    if not a.keep:
        shutil.rmtree(scratch, ignore_errors=True)
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
