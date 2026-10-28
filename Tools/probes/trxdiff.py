"""Per-test diff of two TRX result files, or of two directories of them.

Compares every test's outcome (Passed / Failed / NotExecuted ...) and, for a test that did not pass, the first line
of its message — the skip reason for an Assert.Inconclusive, the assertion for a failure. Two runs with the same
counts can still differ in which tests were skipped and why; that is what this prints.

    python Tools/probes/trxdiff.py <old.trx> <new.trx>
    python Tools/probes/trxdiff.py <old-dir> <new-dir>          # pairs the .trx files by relative path, recursively
    python Tools/probes/trxdiff.py <old-dir> <new-dir> --pattern "results_winbox*.trx"
    python Tools/probes/trxdiff.py <old> <new> --ignore-reasons  # outcomes only
    python Tools/probes/trxdiff.py <baseline-dir> <subset-dir> --common   # a partial run against a full baseline

Exit code: 0 when every paired test has the same outcome (and reason, unless --ignore-reasons), 1 when anything
differs, 2 on a usage error. A trx present on one side only is reported and counts as a difference, unless --common
pairs only the files both sides have (for a run of a few legs against a full-matrix baseline).
"""

import argparse
import fnmatch
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def load(path):
    """Returns {test key: (outcome, reason)}. The key is class + test name; a repeated key (a data row whose
    display name does not differ) gets a #n suffix in file order."""
    root = ET.parse(path).getroot()
    classes = {}
    for ut in root.iterfind("t:TestDefinitions/t:UnitTest", NS):
        tm = ut.find("t:TestMethod", NS)
        classes[ut.get("id")] = tm.get("className") if tm is not None else ""
    tests = {}
    for r in root.iterfind("t:Results/t:UnitTestResult", NS):
        key = f"{classes.get(r.get('testId'), '')}.{r.get('testName')}"
        n, base = 1, key
        while key in tests:
            n += 1
            key = f"{base}#{n}"
        outcome = r.get("outcome", "")
        reason = ""
        if outcome != "Passed":
            msg = r.find("t:Output/t:ErrorInfo/t:Message", NS)
            if msg is not None and msg.text:
                reason = msg.text.strip().splitlines()[0].strip()
        tests[key] = (outcome, reason)
    return tests


def counts(tests):
    c = {}
    for outcome, _ in tests.values():
        c[outcome] = c.get(outcome, 0) + 1
    return " ".join(f"{k}={v}" for k, v in sorted(c.items()))


def diff_pair(name, old_path, new_path, ignore_reasons, out):
    old, new = load(old_path), load(new_path)
    lines = []
    for key in sorted(set(old) | set(new)):
        if key not in new:
            lines.append(f"  - {key}: {old[key][0]} (only in old)")
        elif key not in old:
            lines.append(f"  + {key}: {new[key][0]} (only in new)")
        else:
            (oo, orr), (no, nr) = old[key], new[key]
            if oo != no:
                lines.append(f"  ~ {key}: {oo} -> {no}")
                for label, text in (("old", orr), ("new", nr)):
                    if text:
                        lines.append(f"      {label}: {text}")
            elif not ignore_reasons and orr != nr:
                lines.append(f"  ? {key}: {oo}, reason changed")
                lines.append(f"      old: {orr}")
                lines.append(f"      new: {nr}")
    status = "DIFFERS" if lines else "same"
    out.write(f"{name}: {status}   old [{counts(old)}]   new [{counts(new)}]\n")
    for line in lines:
        out.write(line + "\n")
    return not lines


def collect(directory, pattern):
    found = {}
    for dirpath, _, files in os.walk(directory):
        for f in files:
            if f.lower().endswith(".trx") and fnmatch.fnmatch(f, pattern):
                full = os.path.join(dirpath, f)
                found[os.path.relpath(full, directory).replace(os.sep, "/")] = full
    return found


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("old")
    p.add_argument("new")
    p.add_argument("--pattern", default="*.trx", help="file-name filter for directory mode (default *.trx)")
    p.add_argument("--ignore-reasons", action="store_true", help="compare outcomes only")
    p.add_argument("--common", action="store_true", help="directory mode: compare only the trx files both sides have")
    a = p.parse_args()

    out = sys.stdout
    if os.path.isfile(a.old) and os.path.isfile(a.new):
        same = diff_pair(os.path.basename(a.new), a.old, a.new, a.ignore_reasons, out)
        return 0 if same else 1
    if not (os.path.isdir(a.old) and os.path.isdir(a.new)):
        sys.stderr.write("old and new must both be .trx files or both be directories\n")
        return 2

    old, new = collect(a.old, a.pattern), collect(a.new, a.pattern)
    same = True
    names = sorted(set(old) & set(new)) if a.common else sorted(set(old) | set(new))
    if a.common:
        out.write(f"comparing {len(names)} trx present on both sides "
                  f"({len(old) - len(names)} only in old, {len(new) - len(names)} only in new, not compared)\n")
    for rel in names:
        if rel not in new:
            out.write(f"{rel}: only in old\n")
            same = False
        elif rel not in old:
            out.write(f"{rel}: only in new\n")
            same = False
        else:
            same &= diff_pair(rel, old[rel], new[rel], a.ignore_reasons, out)
    if not names:
        sys.stderr.write("no .trx files matched\n")
        return 2
    return 0 if same else 1


if __name__ == "__main__":
    sys.exit(main())
