import glob, re
for path in glob.glob('materials/**/*.vtex', recursive=True):
    t = open(path, newline='').read()
    t2 = re.sub(r'("m_fileName" "string" ")[^"]*(")',
                lambda m: m.group(1) + m.group(0)[len(m.group(1)):-1].replace(chr(92), '/') + m.group(2),
                t)
    open(path, 'w', newline='\n').write(t2)
    print('fixed', path)
