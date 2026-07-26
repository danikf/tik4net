#!/usr/bin/env python3
# jg_analyze.py - parse WinBox .jg plugin catalogs (JS object literals) and
# extract the M2 protocol map: (handler path, command, field key/type).
#
# .jg format discovery (2026-06-07): plain-text JS object literal, e.g.
#   [{name:'IP Scan',type:'query',path:[ 101,1 ],startcmd:1,cancelcmd:2,
#     c:[{name:'Address',type:'ipaddr',id:'u1',width:90}, ...]}]
#
# Mapping to M2:
#   path:[a,b]      -> SYS_TO handler array (0xFF0001)
#   cmd/startcmd/.. -> SYS_CMD (0xFF0007) command number
#   id:'<pfx><hex>' -> field: type=prefix letters, key=hex suffix
#
# Usage: python jg_analyze.py <dir-with-jg> [--json out.json]

import sys, os, json, glob, re
from collections import defaultdict, Counter

# ---------------------------------------------------------------- tokenizer/parser
class JgParser:
    """Tolerant recursive-descent parser for WinBox .jg JS-object-literal text."""
    def __init__(self, text):
        self.s = text
        self.i = 0
        self.n = len(text)

    def ws(self):
        while self.i < self.n and self.s[self.i] in ' \t\r\n':
            self.i += 1

    def parse(self):
        self.ws()
        v = self.value()
        return v

    def value(self):
        self.ws()
        c = self.s[self.i]
        if c == '{':
            return self.obj()
        if c == '[':
            return self.arr()
        if c == "'" or c == '"':
            return self.string()
        # number or bareword
        return self.scalar()

    def obj(self):
        assert self.s[self.i] == '{'
        self.i += 1
        d = {}
        self.ws()
        if self.s[self.i] == '}':
            self.i += 1
            return d
        while True:
            self.ws()
            # key: identifier, number, or quoted string
            if self.s[self.i] in "'\"":
                key = self.string()
            else:
                m = re.match(r"[^:,}\]\s]+", self.s[self.i:])
                key = m.group(0)
                self.i += len(key)
            self.ws()
            assert self.s[self.i] == ':', f"expected : at {self.i}: ...{self.s[self.i-20:self.i+20]}..."
            self.i += 1
            val = self.value()
            # store; duplicate keys -> keep list
            if key in d:
                if not isinstance(d[key], _Multi):
                    d[key] = _Multi([d[key]])
                d[key].append(val)
            else:
                d[key] = val
            self.ws()
            if self.s[self.i] == ',':
                self.i += 1
                self.ws()
                if self.s[self.i] == '}':  # trailing comma
                    self.i += 1
                    return d
                continue
            if self.s[self.i] == '}':
                self.i += 1
                return d
            raise ValueError(f"obj parse err at {self.i}: ...{self.s[self.i-20:self.i+20]}...")

    def arr(self):
        assert self.s[self.i] == '['
        self.i += 1
        a = []
        self.ws()
        if self.s[self.i] == ']':
            self.i += 1
            return a
        while True:
            a.append(self.value())
            self.ws()
            if self.s[self.i] == ',':
                self.i += 1
                self.ws()
                if self.s[self.i] == ']':
                    self.i += 1
                    return a
                continue
            if self.s[self.i] == ']':
                self.i += 1
                return a
            raise ValueError(f"arr parse err at {self.i}: ...{self.s[self.i-20:self.i+20]}...")

    def string(self):
        q = self.s[self.i]
        self.i += 1
        buf = []
        while self.i < self.n:
            c = self.s[self.i]
            if c == '\\':
                buf.append(self.s[self.i+1])
                self.i += 2
                continue
            if c == q:
                self.i += 1
                return ''.join(buf)
            buf.append(c)
            self.i += 1
        raise ValueError("unterminated string")

    def scalar(self):
        m = re.match(r"[^,}\]\s]+", self.s[self.i:])
        tok = m.group(0)
        self.i += len(tok)
        # int?
        if re.fullmatch(r"-?\d+", tok):
            return int(tok)
        return tok  # bareword

class _Multi(list):
    pass

# ---------------------------------------------------------------- field id decode
# prefix letters -> meaning (lowercase=scalar, uppercase=array of same)
PREFIX = {
    'u':'u32', 'U':'u32[]',
    's':'string', 'S':'string[]',
    'b':'bool', 'B':'bool[]',
    'r':'raw', 'R':'raw[]',
    'm':'addr', 'M':'addr[]',
    'a':'ip6', 'A':'ip6[]',
    'x':'u64', 'X':'u64[]',
    'q':'u64', 'i':'i32', 'd':'dur', 't':'time',
}

def decode_id(idstr):
    """'s10006' -> ('string', 0x10006, 's'); returns (type, key, prefix) or None.
    Prefix is exactly ONE leading letter (the type code); the rest is the hex key.
    (a-f are hex digits, so a multi-letter prefix would wrongly swallow the key.)"""
    m = re.fullmatch(r"([a-zA-Z])([0-9a-fA-F]+)", idstr)
    if not m:
        return None
    pfx, hexpart = m.group(1), m.group(2)
    try:
        key = int(hexpart, 16)
    except ValueError:
        return None
    typ = PREFIX.get(pfx, '?'+pfx)
    return (typ, key, pfx)

# ---------------------------------------------------------------- catalog extraction
CMD_KEYS = ('cmd','startcmd','pollcmd','cancelcmd','setcmd','addcmd','removecmd')

def walk(node, path_owner, ops, prefixes, types_unknown):
    """Recursively walk; attribute id-fields to nearest enclosing path-node."""
    if isinstance(node, dict):
        has_path = 'path' in node and isinstance(node['path'], list)
        owner = path_owner
        if has_path:
            op = {
                'path': node['path'],
                'name': node.get('name') or node.get('title') or '',
                'title': node.get('title',''),
                'group': node.get('group',''),
                'type': node.get('type',''),
                'cmds': {k: node[k] for k in CMD_KEYS if k in node},
                'fields': [],
            }
            ops.append(op)
            owner = op
        # collect this node's id field
        if 'id' in node and isinstance(node['id'], str):
            dec = decode_id(node['id'])
            if dec:
                prefixes[dec[2]] += 1
                if owner is not None:
                    owner['fields'].append({
                        'name': node.get('name') or node.get('title') or '',
                        'id': node['id'], 'type': dec[0], 'key': dec[1],
                        'ro': node.get('ro',0), 'ftype': node.get('type',''),
                    })
            else:
                types_unknown[node['id']] += 1
        for k, v in node.items():
            if k == 'id':
                continue
            walk(v, owner, ops, prefixes, types_unknown)
    elif isinstance(node, list):
        for it in node:
            walk(it, path_owner, ops, prefixes, types_unknown)

def analyze_file(fp):
    text = open(fp, 'r', encoding='utf-8', errors='replace').read()
    tree = JgParser(text).parse()
    ops, prefixes, unknown = [], Counter(), Counter()
    walk(tree, None, ops, prefixes, unknown)
    return ops, prefixes, unknown

# ---------------------------------------------------------------- main
def fmt_path(p):
    return '[' + ','.join(str(x) for x in p) + ']'

def main():
    d = sys.argv[1]
    jsonout = None
    if '--json' in sys.argv:
        jsonout = sys.argv[sys.argv.index('--json')+1]
    files = sorted(glob.glob(os.path.join(d, '*.jg')))
    all_ops = []
    all_prefixes = Counter()
    all_unknown = Counter()
    print(f"=== Analyzing {len(files)} .jg files in {d} ===\n")
    for fp in files:
        try:
            ops, prefixes, unknown = analyze_file(fp)
        except Exception as e:
            print(f"  PARSE FAIL {os.path.basename(fp)}: {e}")
            continue
        all_prefixes.update(prefixes)
        all_unknown.update(unknown)
        for op in ops:
            op['file'] = os.path.basename(fp)
        all_ops.extend(ops)
        print(f"  {os.path.basename(fp):16} -> {len(ops):4} path-ops")

    print(f"\n=== Total {len(all_ops)} path-operations ===")
    print(f"\n=== id prefix histogram (type letters) ===")
    for pfx, cnt in all_prefixes.most_common():
        print(f"  {pfx:3} ({PREFIX.get(pfx,'?'):8}) x{cnt}")
    if all_unknown:
        print(f"\n=== unparsable ids (sample) ===")
        for ids, cnt in all_unknown.most_common(20):
            print(f"  {ids!r} x{cnt}")

    # unique handler paths
    paths = Counter(fmt_path(op['path']) for op in all_ops)
    print(f"\n=== unique handler paths ({len(paths)}) ===")
    for p, cnt in sorted(paths.items(), key=lambda kv: -kv[1]):
        # show a sample name
        sample = next((o for o in all_ops if fmt_path(o['path'])==p), None)
        print(f"  {p:14} x{cnt:3}  e.g. {sample['name']!r} type={sample['type']!r} cmds={sample['cmds']}")

    if jsonout:
        json.dump(all_ops, open(jsonout,'w',encoding='utf-8'), indent=1, ensure_ascii=False)
        print(f"\nWrote {jsonout} ({len(all_ops)} ops)")

# window vs data-source-ref classification
REF_TYPES = {'dynamic','static','slot','defenum','queryenum','number','bitmap',
             'cond','enm','opt','not','or','and'}

def is_window(op):
    """A real UI window/operation (owns fields), not a data-source reference."""
    return bool(op['fields']) or bool(op['cmds']) or (
        op['name'] and op['type'] not in REF_TYPES)

def load_ops(d):
    out = []
    for fp in sorted(glob.glob(os.path.join(d,'*.jg'))):
        try:
            ops,_,_ = analyze_file(fp)
        except Exception as e:
            print(f"  PARSE FAIL {os.path.basename(fp)}: {e}"); continue
        for op in ops: op['file']=os.path.basename(fp)
        out.extend(ops)
    return out

def cmd_detail(d, pathstr):
    target = [int(x) for x in pathstr.replace('[','').replace(']','').split(',')]
    ops = load_ops(d)
    hits = [o for o in ops if o['path']==target and is_window(o)]
    print(f"=== windows on path {fmt_path(target)} in {d}: {len(hits)} ===\n")
    for o in hits:
        print(f"[{o['file']}] name={o['name']!r} title={o['title']!r} type={o['type']!r} "
              f"group={o['group']!r} cmds={o['cmds']}")
        for f in o['fields']:
            ro = ' RO' if f['ro'] else ''
            print(f"    key=0x{f['key']:<6x} {f['type']:9} id={f['id']:10} "
                  f"ftype={f['ftype']:14} {f['name']!r}{ro}")
        print()

def cmd_diff(da, db):
    """Stability check: for each handler path, compare field-key sets across versions."""
    oa, ob = load_ops(da), load_ops(db)
    def by_path(ops):
        m = defaultdict(set)
        for o in ops:
            if is_window(o):
                for f in o['fields']:
                    m[fmt_path(o['path'])].add(f['key'])
        return m
    ma, mb = by_path(oa), by_path(ob)
    pa, pb = set(ma), set(mb)
    print(f"=== path stability {os.path.basename(da)} vs {os.path.basename(db)} ===")
    print(f"  paths in A: {len(pa)}  in B: {len(pb)}  common: {len(pa&pb)}")
    print(f"  only in A (removed?): {len(pa-pb)}")
    print(f"  only in B (added):    {len(pb-pa)}")
    # for common paths, are A's keys a subset of B's? (expand-not-rename hypothesis)
    subset = sum(1 for p in (pa&pb) if ma[p] <= mb[p])
    grew   = sum(1 for p in (pa&pb) if ma[p] < mb[p])
    print(f"  common paths where A-keys ⊆ B-keys (stable/expanded): {subset}/{len(pa&pb)}")
    print(f"  common paths where B added keys:                     {grew}")
    viol = [p for p in (pa&pb) if not (ma[p] <= mb[p])]
    print(f"  paths where a key DISAPPEARED in B: {len(viol)}")
    for p in sorted(viol)[:15]:
        print(f"    {p}: A-only keys {sorted(hex(k) for k in (ma[p]-mb[p]))[:8]}")

def cmd_report(d, outfp):
    """Write a human-readable windows-with-fields catalog to a file."""
    ops = [o for o in load_ops(d) if is_window(o)]
    ops.sort(key=lambda o: (o['file'], o['path']))
    with open(outfp, 'w', encoding='utf-8') as w:
        w.write(f"# WinBox .jg catalog (windows with fields) — {os.path.basename(d)}\n")
        w.write(f"# {len(ops)} windows. path=SYS_TO handler, cmds=SYS_CMD, key/type per field.\n\n")
        for o in ops:
            w.write(f"{fmt_path(o['path']):12} {o['type']:10} {o['name']!r}"
                    f"{(' cmds='+str(o['cmds'])) if o['cmds'] else ''}  [{o['file']}]\n")
            for f in o['fields']:
                ro = ' RO' if f['ro'] else ''
                w.write(f"      0x{f['key']:<6x} {f['type']:8} {f['ftype']:14} {f['name']!r}{ro}\n")
    print(f"wrote {outfp} ({len(ops)} windows)")

if __name__ == '__main__':
    if len(sys.argv) >= 2 and sys.argv[1] == 'report':
        cmd_report(sys.argv[2], sys.argv[3])
    elif len(sys.argv) >= 2 and sys.argv[1] == 'detail':
        cmd_detail(sys.argv[2], sys.argv[3])
    elif len(sys.argv) >= 2 and sys.argv[1] == 'diff':
        cmd_diff(sys.argv[2], sys.argv[3])
    else:
        main()
