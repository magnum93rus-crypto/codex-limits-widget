import pathlib
import shutil
import subprocess

root=pathlib.Path(__file__).parent
source=root/'src'/'LimitsModel.cs'
original=source.read_bytes()
old=b'return dictation?Rectangle.Empty:Between(left,right,scale);'
new=b'return !dictation?Rectangle.Empty:Between(left,right,scale);'
assert original.count(old)==1
mutated=original.replace(old,new)
try:
    source.write_bytes(mutated)
    result=subprocess.run([shutil.which('pwsh'),' -NoProfile'.strip(),'-File',str(root/'test.ps1')],capture_output=True,text=True,errors='replace')
    failures=[line for line in result.stdout.splitlines() if line.startswith('FAIL ')]
    if result.returncode==0 or not any('dictation hides badge' in line for line in failures):
        raise RuntimeError('Expected dictation regression was not detected')
    print('Mutation detected: inverted dictation visibility')
    for line in failures: print(line)
finally:
    if source.read_bytes()!=mutated: raise RuntimeError('Source changed externally; inspect before restoring')
    source.write_bytes(original)
