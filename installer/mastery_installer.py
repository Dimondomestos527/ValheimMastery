#!/usr/bin/env python3

"""Valheim Mastery release installer. Standard library; no game launch."""

import argparse, hashlib, importlib.util, io, json, os, platform, re, shutil, struct, subprocess, sys, tempfile, urllib.request, uuid, zipfile

from pathlib import Path, PurePosixPath

REPO = 'Dimondomestos527/ValheimMastery'

MAX = 64*1024*1024
FETCH_TIMEOUT=60

BEP = {

 'win32': ('BepInEx_win_x64_5.4.23.5.zip','82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'),

 'darwin': ('BepInEx_macos_universal_5.4.23.5.zip','01c2ae782eb016dfd6c345a18dbd2dcafffb3d9d318449d6486689f426b4a323')}

def sha(b): return hashlib.sha256(b).hexdigest()

def fetch(url):

 with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent':'ValheimMastery-Installer/2'}),timeout=FETCH_TIMEOUT) as r: b=r.read(MAX+1)

 if len(b)>MAX: raise ValueError('Download too large')

 return b

def checked(url, digest):

 b=fetch(url)

 if sha(b)!=digest.lower(): raise ValueError('Download checksum mismatch')

 return b

def steam_busy():

 if sys.platform=='win32':
  import csv
  output=subprocess.check_output([str(Path(os.environ.get('SystemRoot','C:/Windows'))/'System32/tasklist.exe'),'/FO','CSV','/NH']).decode(errors='replace')
  return any(row and row[0].casefold()=='steam.exe' for row in csv.reader(output.splitlines()))
 if sys.platform!='darwin': return False

 return any(Path(line.strip()).name.casefold() in {'steam','steam_osx'} for line in subprocess.check_output(['ps','-axo','comm'],text=True).splitlines())

def allowed_config(path):

 path=Path(path).absolute()

 for steam in steam_roots():

  for config in (Path(steam)/'userdata').glob('*/config/localconfig.vdf'):

   if config.absolute()==path and not any(p.is_symlink() for p in [config,*config.parents]): return path

 raise ValueError('Outside recognized Steam user configuration')

def busy(variant=None):

 if sys.platform=='win32':

  import csv

  names=[r[0].lower() for r in csv.reader(subprocess.check_output([str(Path(os.environ.get('SystemRoot','C:/Windows'))/'System32/tasklist.exe'),'/FO','CSV','/NH']).decode(errors='replace').splitlines()) if r]

 else: names=[Path(r.strip()).name.lower() for r in subprocess.check_output(['ps','-axo','comm'],text=True).splitlines()]

 blocked={'valheim','valheim.exe'} if variant=='Client' else {'valheim_server','valheim_server.exe'} if variant=='Server' else {'valheim','valheim.exe','valheim_server','valheim_server.exe'}
 return any(n in blocked for n in names)

def safe(root, name):

 p=PurePosixPath(name)

 if not name or '\\' in name or ':' in name or p.is_absolute() or '..' in p.parts or str(p)!=name: raise ValueError('Unsafe path')

 target=root/name

 for part in [target,*target.parents]:

  if part==root.parent: break

  if part.is_symlink() or (part.exists() and getattr(part.stat(),'st_file_attributes',0)&0x400): raise ValueError('Symlink/reparse path')

 target.resolve().relative_to(root.resolve())

 return target

LEGACY_NAMES={'BepInEx/plugins/ValheimMastery.dll','BepInEx/plugins/ValheimMasteryPoC.dll'}
LEGACY_HASHES={
 'f10ad55cc838217d1782c23ee18a8f80f254d3b3ecc1dd8a3194aab3707d5751':'Client',
 'ba25efa8736bc329f50ca369537f4e03a7d28cb46eac70cb06cfec829d048c1f':'Server',
 'dbb0d271f7bf0e7b91c1af88d3e7599fd4842358b6b8eeb978cf041a159f4568':'Client',
 '1be438f94ad93c272fa7de4958ff6ad241ba248bd681a481c3e8e717c09d369c':'Server'}

def legacy_candidates(root,variant):
 result={};base=safe(root,'BepInEx/plugins')
 if not base.exists():return result
 for path in base.rglob('*'):
  name=path.relative_to(root).as_posix();safe(root,name)
  if not path.is_file() or path.suffix.casefold()!='.dll' or name=='BepInEx/plugins/ValheimMastery/ValheimMastery.dll':continue
  if path.stat().st_size>MAX:raise ValueError('Plugin DLL too large to inspect safely: '+name)
  data=path.read_bytes();digest=sha(data)
  suspected=path.stem.casefold() in {'valheimmastery','valheimmasterypoc'} or b'domestos.valheim.mastery' in data or b'ValheimMastery' in data
  if not suspected:continue
  if name not in LEGACY_NAMES or LEGACY_HASHES.get(digest)!=variant:
   raise ValueError('Unknown/ambiguous legacy Mastery DLL preserved; review this path before installing: '+name)
  result[name]=data
 return result

def owned(name):

 return name in LEGACY_NAMES or name=='BepInEx/config/domestos.valheim.mastery.cfg' or name in {'.doorstop_version','winhttp.dll','doorstop_config.ini','libdoorstop.dylib','run_bepinex.sh','Launch-Mastery.command','Launch-Mastery.exe'} or name.startswith('.mastery-installer/updater/') or name.startswith('BepInEx/core/') or name.startswith('BepInEx/plugins/ValheimMastery/')

def unpack(blob):

 result={}

 with zipfile.ZipFile(io.BytesIO(blob)) as z:

  if sum(i.file_size for i in z.infolist())>MAX: raise ValueError('Expanded ZIP too large')

  for i in z.infolist():

   if i.is_dir(): continue

   if i.filename.casefold() in {n.casefold() for n in result} or (i.external_attr>>16)&0o170000==0o120000: raise ValueError('ZIP duplicate/symlink')

   safe(Path(tempfile.gettempdir())/'mastery-path-check',i.filename)

   result[i.filename]=z.read(i)

 return result

def atomic(path,b):

 path.parent.mkdir(parents=True,exist_ok=True)

 tmp=path.with_name(path.name+'.tmp-'+uuid.uuid4().hex)

 try: tmp.write_bytes(b); os.replace(tmp,path)

 finally:

  if tmp.exists(): tmp.unlink()

def state_bytes(state): return json.dumps(state,indent=2,ensure_ascii=False).encode('utf-8')

def load(root):

 p=safe(root,'.mastery-installer/state.json')

 if not p.exists(): return None

 s=json.loads(p.read_bytes())

 if s.get('schema')!=2 or s.get('product')!='ValheimMastery' or not isinstance(s.get('files'),dict): raise ValueError('Invalid ownership journal')

 for n,r in s['files'].items():

  retired=r.get('kind')=='retired' and n in LEGACY_NAMES and r.get('installed') is None
  if not owned(n) or (not retired and not re.fullmatch('[0-9a-f]{64}',r.get('installed',''))): raise ValueError('Invalid owned entry')

  safe(root,n)

  if r.get('original') is not None and not re.fullmatch('[0-9a-f]{64}',r['original']): raise ValueError('Invalid backup digest')

 return s

def recover(root):

 p=safe(root,'.mastery-installer/pending.json')

 if not p.exists(): return

 data=json.loads(p.read_bytes()); prepared=[]

 for n,record in data['before'].items():

  if not (owned(n) or n=='.mastery-installer/state.json'): raise ValueError('Invalid recovery path')

  dst=safe(root,n); after=data['after'][n]

  for h in [record,after]:

   if h is not None and not re.fullmatch('[0-9a-f]{64}',h): raise ValueError('Invalid recovery digest')

  current=sha(dst.read_bytes()) if dst.exists() else None

  if current not in {record,after}: raise ValueError('Owned file changed after interruption; preserved: '+n)

  b=None

  if record is not None:

   b=safe(root,'.mastery-installer/objects/'+record).read_bytes()

   if sha(b)!=record: raise ValueError('Recovery backup corrupt')

  prepared.append((dst,b))

 if data.get('external'):

  if steam_busy(): raise RuntimeError('Close Steam normally before pending launch-configuration recovery')

  item=data['external']; config=allowed_config(item['path'])

  if any(not re.fullmatch('[0-9a-f]{64}',item[k]) for k in ['before','after']): raise ValueError('Invalid Steam backup digest')

  if sha(config.read_bytes()) not in {item['before'],item['after']}: raise ValueError('Steam configuration changed after interruption; preserved')

  b=safe(root,'.mastery-installer/objects/'+item['before']).read_bytes()

  if sha(b)!=item['before']: raise ValueError('Steam recovery backup corrupt')

  prepared.append((config,b))

 for dst,b in prepared:

  if b is None:

   if dst.exists(): dst.unlink()

  else: atomic(dst,b)

 p.unlink()

class InstallerLock:

 def __init__(self,root): self.path=safe(root,'.mastery-installer/lock'); self.handle=None

 def acquire(self):

  self.handle=self.path.open('a+b'); self.handle.seek(0,2)

  if self.handle.tell()==0: self.handle.write(b'0'); self.handle.flush()

  self.handle.seek(0)

  try:

   if sys.platform=='win32':

    import msvcrt; msvcrt.locking(self.handle.fileno(),msvcrt.LK_NBLCK,1)

   else:

    import fcntl; fcntl.flock(self.handle.fileno(),fcntl.LOCK_EX|fcntl.LOCK_NB)

  except OSError:

   self.handle.close(); self.handle=None; raise RuntimeError('Another installer is using this folder')

 def close(self):

  if self.handle:

   if sys.platform=='win32':

    import msvcrt; self.handle.seek(0); msvcrt.locking(self.handle.fileno(),msvcrt.LK_UNLCK,1)

   else:

    import fcntl; fcntl.flock(self.handle.fileno(),fcntl.LOCK_UN)

   self.handle.close(); self.handle=None

def recover_locked(root):

 # Pure mod recovery must work with Steam open; recover() guards external VDF journals.

 control=safe(root,'.mastery-installer')

 if not control.exists(): return

 lock=InstallerLock(root); lock.acquire()

 try: recover(root)

 finally: lock.close()

def transaction(root, changes, state, inject=None, expected=None, external=None, expected_files=None):

 if busy(state.get('variant')): raise RuntimeError('Close the selected Valheim client/server normally before continuing')
 if external and steam_busy(): raise RuntimeError('Close Steam normally before changing launch options')

 control=safe(root,'.mastery-installer'); control.mkdir(exist_ok=True)

 lock=InstallerLock(root); lock.acquire()

 try:

  recover(root)

  if load(root)!=expected: raise RuntimeError('Ownership changed during preparation; retry')

  for name,digest in (expected_files or {}).items():
   path=safe(root,name);current=sha(path.read_bytes()) if path.is_file() else None
   if current!=digest:raise ValueError('File changed during preparation; preserved: '+name)

  before={}

  changes=dict(changes); changes['.mastery-installer/state.json']=state_bytes(state)

  for n,b in changes.items():

   if n!='.mastery-installer/state.json' and not owned(n): raise ValueError('Outside owned scope')

   p=safe(root,n)

   if p.exists() and not p.is_file(): raise ValueError('Destination is not a file')

   if p.exists():

    old=p.read_bytes(); h=sha(old); atomic(safe(root,'.mastery-installer/objects/'+h),old); before[n]=h

   else: before[n]=None

  journal={'before':before,'after':{n:sha(b) if b is not None else None for n,b in changes.items()}}

  if external:

   config,new,expected_hash=external; config=allowed_config(config); previous=config.read_bytes()

   if sha(previous)!=expected_hash: raise ValueError('Steam configuration changed; preserved')

   h=sha(previous); atomic(safe(root,'.mastery-installer/objects/'+h),previous)

   journal['external']={'path':str(config),'before':h,'after':sha(new)}

  atomic(safe(root,'.mastery-installer/pending.json'),state_bytes(journal))

  if busy(state.get('variant')): raise RuntimeError('Selected game/server started during staging')

  try:

   if external:

    if steam_busy(): raise RuntimeError('Steam started during installation')

    atomic(allowed_config(external[0]),external[1])

   for index,(n,b) in enumerate(changes.items()):

    p=safe(root,n)

    if b is None:

     if p.exists(): p.unlink()

    else: atomic(p,b)

    if inject: inject(index,n)

   for n,b in changes.items():

    p=safe(root,n)

    if b is not None and sha(p.read_bytes())!=sha(b): raise ValueError('Installed checksum mismatch')

   safe(root,'.mastery-installer/pending.json').unlink()

  except BaseException: recover(root); raise

 finally: lock.close()

def apply(root, files, variant, version, inject=None, launch=None, external=None, startup=None, expected_files=None, retire=None, release_commit=None):

 root=Path(root).absolute()

 if not root.is_dir(): raise ValueError('Game folder does not exist')

 recover_locked(root)

 old=load(root)

 if old and old['variant']!=variant: raise ValueError('Client/server journal mismatch')

 records=dict(old['files']) if old else {}

 for n,b in files.items():

  if not owned(n): raise ValueError('Outside installer scope')

  p=safe(root,n)

  if n in records:

   if records[n]['kind']!='preference' and (not p.is_file() or sha(p.read_bytes())!=records[n]['installed']): raise ValueError('Owned file changed; preserve it and stop: '+n)

   records[n]=dict(records[n],installed=sha(b))

  else:

   original=None

   if p.exists():

    if not p.is_file(): raise ValueError('Destination is not a file')

    data=p.read_bytes(); original=sha(data)

    atomic(safe(root,'.mastery-installer/objects/'+original),data)

   records[n]={'installed':sha(b),'original':original,'kind':'preference' if n=='BepInEx/config/domestos.valheim.mastery.cfg' else ('startup' if n.startswith('.mastery-installer/updater/') or n in {'Launch-Mastery.exe','Launch-Mastery.command'} else ('mod' if n.startswith('BepInEx/plugins/ValheimMastery/') else 'framework'))}

 # Do not rewrite identical files (in particular the currently running launcher).
 changes={n:b for n,b in files.items() if not safe(root,n).is_file() or sha(safe(root,n).read_bytes())!=sha(b)}

 expected_files=dict(expected_files or {})
 for name,data in (retire or {}).items():
  digest=sha(data)
  if name not in LEGACY_NAMES or LEGACY_HASHES.get(digest)!=variant:raise ValueError('Unverified legacy retirement')
  path=safe(root,name)
  if not path.is_file() or sha(path.read_bytes())!=digest:raise ValueError('Legacy DLL changed; preserved')
  atomic(safe(root,'.mastery-installer/objects/'+digest),data)
  records[name]={'installed':None,'original':digest,'kind':'retired'}
  changes[name]=None;expected_files[name]=digest

 for n in list(records):

  if records[n]['kind'] in {'mod','startup'} and n not in files:

   p=safe(root,n)

   if not p.is_file() or sha(p.read_bytes())!=records[n]['installed']: raise ValueError('Stale owned file changed')

   original=records[n]['original']

   changes[n]=safe(root,'.mastery-installer/objects/'+original).read_bytes() if original else None

   if original and sha(changes[n])!=original: raise ValueError('Original backup corrupt')

   del records[n]

 state={'schema':2,'product':'ValheimMastery','variant':variant,'version':version,'files':records}

 if launch is not None: state['steam_launch']=launch
 elif old and (old.get('steam_launch') or old.get('mac_launch')): state['steam_launch']=old.get('steam_launch') or old['mac_launch']
 if startup is not None and startup is not False: state['startup']=startup
 elif startup is None and old and old.get('startup'): state['startup']=old['startup']
 if release_commit is not None:state['release_commit']=release_commit
 elif old and old.get('release_commit') and version==old.get('version') and all(n not in changes for n in files if n.startswith('BepInEx/plugins/ValheimMastery/')):
  state['release_commit']=old['release_commit']

 transaction(root,changes,state,inject,expected=old,external=external,expected_files=expected_files)

 return state

def uninstall(root, inject=None):

 root=Path(root).absolute(); recover_locked(root); old=load(root)

 if old is None: raise ValueError('No installer ownership journal; refusing to guess')

 foreign=[]

 for folder in ['BepInEx/plugins','BepInEx/patchers']:

  base=safe(root,folder)

  if base.exists():

   for p in base.rglob('*'):

    if p.is_file():

     n=p.relative_to(root).as_posix()

     if n not in old['files']: foreign.append(n)

 for n,r in old['files'].items():

  if r['kind'] in {'mod','retired'} and n.lower().endswith('.dll'):

   p=safe(root,n)

   if r['original'] or (p.exists() and (not p.is_file() or sha(p.read_bytes())!=r['installed'])): foreign.append(n)

 changes={}; keep={}

 for n,r in old['files'].items():

  p=safe(root,n)

  if r['kind']=='preference': keep[n]=r; continue

  if r['kind']=='framework' and foreign: keep[n]=r; continue

  if p.exists() and (not p.is_file() or sha(p.read_bytes())!=r['installed']): keep[n]=r; print('Preserved changed file:',n); continue

  b=None

  if r['original']:

   b=safe(root,'.mastery-installer/objects/'+r['original']).read_bytes()

   if sha(b)!=r['original']: raise ValueError('Original backup corrupt')

  changes[n]=b

 external=None; launch=old.get('steam_launch') or old.get('mac_launch')

 if launch and (not foreign or launch.get('startup')):

  import steam_launch

  config=allowed_config(launch['path']); b=config.read_bytes(); text=b.decode('utf-8')

  if steam_launch.launch_value(text)[0]==launch['installed']:

   restored=launch.get('fallback') if foreign and sys.platform=='darwin' and launch.get('startup') else launch['original']
   external=(config,steam_launch.set_launch(text,restored).encode('utf-8'),sha(b))

  else: print('Preserved user-changed Steam launch options')

 final=dict(old,files=keep,version='uninstalled'); final.pop('startup',None)
 transaction(root,changes,final,inject,expected=old,external=external)

 print('Mastery removed. User configs, saves and unrelated mods preserved.')

 if foreign: print('BepInEx retained because other mod files exist.')

 return keep

# Small strict VDF reader; do not recurse the disk or modify Steam data here.

def vdf(text):

 tokens=re.findall(r'"(?:\\.|[^"\\])*"|[{}]|[^\s{}"]+',re.sub(r'//[^\n]*','',text))

 pos=0

 def word(t): return re.sub(r'\\([\\"])',r'\1',t[1:-1]) if t.startswith('"') else t

 def obj(nested=False):

  nonlocal pos

  out={}

  while pos<len(tokens):

   t=tokens[pos]; pos+=1

   if t=='}':

    if not nested: raise ValueError('Unexpected VDF brace')

    return out

   if t=='{' or pos>=len(tokens): raise ValueError('Invalid VDF')

   key=word(t); t=tokens[pos]; pos+=1

   if key in out: raise ValueError('Duplicate VDF key')

   out[key]=obj(True) if t=='{' else word(t)

  if nested: raise ValueError('Unclosed VDF object')

  return out

 return obj()

def steam_roots():

 if sys.platform=='win32':

  import winreg

  out=[]

  for hive,key,value in [(winreg.HKEY_CURRENT_USER,r'Software\Valve\Steam','SteamPath'),(winreg.HKEY_LOCAL_MACHINE,r'SOFTWARE\WOW6432Node\Valve\Steam','InstallPath')]:

   try:

    with winreg.OpenKey(hive,key) as k: out.append(Path(winreg.QueryValueEx(k,value)[0]))

   except OSError: pass

  return out

 return [Path.home()/'Library/Application Support/Steam']

def discover(roots, variant):

 # IDs confirmed by appmanifest filenames; clients and servers are never interchangeable.

 appid='892970' if variant=='Client' else '896660'; result=[]

 for steam in roots:

  libraries=[Path(steam)]

  config=Path(steam)/'steamapps/libraryfolders.vdf'

  if config.exists():

   for value in vdf(config.read_text(encoding='utf-8-sig')) .get('libraryfolders',{}).values():

    path=value.get('path') if isinstance(value,dict) else value

    if isinstance(path,str) and Path(path).is_dir(): libraries.append(Path(path))

  for library in libraries:

   manifest=library/('steamapps/appmanifest_'+appid+'.acf')

   if manifest.is_file():

    data=vdf(manifest.read_text(encoding='utf-8-sig')).get('AppState',{})

    name=data.get('installdir','')

    if data.get('appid')!=appid or not name or '/' in name or '\\' in name or name in {'.','..'}: continue

    target=library/'steamapps/common'/name

    if target.is_dir() and target not in result: result.append(target)

 return result

def choose(choices):

 if len(choices)==1: print('Found:',choices[0]); return choices[0]

 if choices:

  for i,p in enumerate(choices,1): print(i,p)

  answer=input('Choose game folder number: ').strip()

  if not answer.isdigit() or not 1<=int(answer)<=len(choices): raise ValueError('Invalid selection')

  return choices[int(answer)-1]

 if sys.platform=='darwin':

  folder=subprocess.check_output(['/usr/bin/osascript','-e','POSIX path of (choose folder with prompt "Choose your Valheim game folder")'],text=True).strip()

 else:

  script='Add-Type -AssemblyName System.Windows.Forms; $d=New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description="Choose Valheim game folder"; if($d.ShowDialog() -eq "OK"){$d.SelectedPath}'

  folder=subprocess.check_output([str(Path(os.environ.get('SystemRoot','C:/Windows'))/'System32/WindowsPowerShell/v1.0/powershell.exe'),'-NoProfile','-STA','-Command',script],text=True).strip()

 if not folder: raise ValueError('No game folder selected')

 return Path(folder)

def select_target(variant):
 answer=input('Game folder: 1 Auto-search Steam (default), 2 Choose folder manually: ').strip()
 if answer in {'','1'}:
  return choose(discover(steam_roots(),variant))
 if answer=='2':
  return choose([])  # Native folder picker; do not search Steam in manual mode.
 raise ValueError('Invalid folder selection mode')

def mac_executable(root):
 import plistlib
 apps=list(root.glob('*.app'))
 if len(apps)!=1 or apps[0].stem.casefold()!='valheim':raise ValueError('Requires exactly one native Valheim.app')
 app=safe(root,apps[0].relative_to(root).as_posix())
 info_path=safe(root,(app/'Contents/Info.plist').relative_to(root).as_posix())
 info=plistlib.loads(info_path.read_bytes());name=info.get('CFBundleExecutable')
 if not isinstance(name,str) or not name or name in {'.','..'} or any(c in name for c in '/\\:\r\n\0'):
  raise ValueError('Invalid Mac executable basename')
 exe=safe(root,(app/'Contents/MacOS'/name).relative_to(root).as_posix())
 if not exe.is_file():raise ValueError('Mac executable missing')
 return exe,app

def identity(root,variant,interactive=True):

 if sys.platform=='win32':

  exe=safe(root,'valheim.exe' if variant=='Client' else 'valheim_server.exe')

  with exe.open('rb') as f:

   if f.read(2)!=b'MZ': raise ValueError('Not a Windows executable')

   f.seek(0x3c); offset=struct.unpack('<I',f.read(4))[0]; f.seek(offset)

   if f.read(4)!=b'PE\0\0' or f.read(2)!=b'\x64\x86': raise ValueError('Requires Windows x64 Valheim')

 else:

  if variant!='Client':raise ValueError('Mac installer targets the native game client')
  exe,app=mac_executable(root)

  description=subprocess.check_output(['/usr/bin/file','-b',str(exe)],text=True)

  if 'x86_64' not in description: raise ValueError('Current BepInEx5 path requires x86_64 game slice')

  if platform.machine()=='arm64' and subprocess.run(['/usr/bin/arch','-x86_64','/usr/bin/true']).returncode:

   if not interactive: raise RuntimeError('Rosetta is required; rerun the interactive Mac installer')
   answer=input('Rosetta is required. Start the Apple installer now? You must accept its license yourself. [y/N] ').strip().lower()

   if answer!='y' or subprocess.run(['/usr/sbin/softwareupdate','--install-rosetta']).returncode: raise RuntimeError('Rosetta was not installed')

   if subprocess.run(['/usr/bin/arch','-x86_64','/usr/bin/true']).returncode: raise RuntimeError('Rosetta validation failed')

# Official BepInEx macOS x64 5.4.23.2 files; preserve a verified legacy
# runtime as foreign/preexisting, never adopt or overwrite it during mod update.
LEGACY_MAC_RUNTIME = {'.doorstop_version': '2744735fee2ee237e29f592f88ab669dc8d478fca31e6da4caf9a4a1b3abb7f1', 'libdoorstop.dylib': 'c1973e0b124b7ca58aed82c6672559f411da214760718eb42b028bd3f8f57e16', 'run_bepinex.sh': '985e6df9ab6b693485fb6f7fcbe85de3bc3355d0c073ed3686171d18a7d500a0', 'BepInEx/core/0Harmony.dll': '1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031', 'BepInEx/core/0Harmony.xml': 'd1f02fc3ada3a13da307de421225bfe56ebe24064370980979391c4be021672f', 'BepInEx/core/0Harmony20.dll': 'd256c5373692a184018f171144712460ced1a6f01562fde26e742b077b36cbd6', 'BepInEx/core/BepInEx.dll': 'c65b42034bc8ffb9f0b336e416dc3884e3f99fc5a5a89eb1f2ff7868412322cd', 'BepInEx/core/BepInEx.Harmony.dll': 'd0739c4a13f369094cb164c205ee4cca5392bdd7241b9f242ee13f0d4c0b1856', 'BepInEx/core/BepInEx.Harmony.xml': 'a04fedf08f7c81f5d01aba6f2840a7ffce50b79bbd24587d8dbe69ab73971d29', 'BepInEx/core/BepInEx.Preloader.dll': '116f8b879b1b87566f5ce30106fc5d5718da69d3870315d184a4460379a765c7', 'BepInEx/core/BepInEx.Preloader.xml': '5ccaffcef1c41292d94931b24f140ca82b47a879e3439e89293285054490eb0a', 'BepInEx/core/BepInEx.xml': 'c0c7799bbaf1e37398f85f0ba8e02d8136c55a3165db87063942e3fedda0a68c', 'BepInEx/core/HarmonyXInterop.dll': '4d6175fa6dfee743423380f62fb5cc7f1811b469748538bd1b974ded34f3f907', 'BepInEx/core/Mono.Cecil.dll': '7ae470288fff4a402899c254d0a76cefef55877f5c54f96e83c797cc5bb6e2f6', 'BepInEx/core/Mono.Cecil.Mdb.dll': '5896d1898f616701fff18f3b2c71e6b844d2390ef9f41e1c5fccce8cb27c698e', 'BepInEx/core/Mono.Cecil.Pdb.dll': '174db44a067f58561510af746f3caeb032037762c57a31c8d9ee32db25174984', 'BepInEx/core/Mono.Cecil.Rocks.dll': '54ac539fb5ddc8b44c0e9acd0fcb7324f89d1a072edf8ebc1b06dd691e3d3927', 'BepInEx/core/MonoMod.RuntimeDetour.dll': '40e49bb314391cd7bddc2644f8553eeba92c194b940836b103df16955c464e0c', 'BepInEx/core/MonoMod.RuntimeDetour.xml': '54887808960d156550b37d602d08847607aa9e908d039f2765fb0b5e79394aa4', 'BepInEx/core/MonoMod.Utils.dll': '9d1495f147ac93c4f81f84538c1a326e8f8a6aefc78d6289d798f3ce1162c5e9', 'BepInEx/core/MonoMod.Utils.xml': '0577b362023a3432d6e8d7934c5eddc3e08fdbb19e191af083e341562c5ede38'}

def framework(root,blob):

 expected={n:b for n,b in unpack(blob).items() if owned(n)}

 markers=['BepInEx/core','winhttp.dll','doorstop_config.ini','.doorstop_version','libdoorstop.dylib','run_bepinex.sh']

 core=safe(root,'BepInEx/core')
 # Uninstall preserves directories/configs: an empty core folder is not a framework.
 core_present=core.exists() and (not core.is_dir() or any(core.iterdir()))
 any_existing=core_present or any(safe(root,n).exists() for n in markers if n!='BepInEx/core')

 if any_existing:

  if 'libdoorstop.dylib' in expected and all(
      safe(root,n).is_file() and sha(safe(root,n).read_bytes())==digest
      for n,digest in LEGACY_MAC_RUNTIME.items()):
   print('Preserving verified existing BepInEx macOS x64 5.4.23.2; updating Mastery only.')
   return {}

  for n,b in expected.items():

   p=safe(root,n)

   # The version marker is advisory on a preexisting Mac loader. Accept its
   # absence only when EVERY other official runtime file is byte-identical.
   # Do not create/adopt it or relax checks on any executable/core/config file.
   if n=='.doorstop_version' and 'libdoorstop.dylib' in expected and not p.exists(): continue
   if not p.is_file() or sha(p.read_bytes())!=sha(b): raise ValueError('Existing BepInEx differs or is incomplete; preserved without overwriting: '+n)

  return {} # Exactly compatible preexisting framework is never adopted.

 return expected

def startup_module(head=None):
 path=Path(__file__).parent/'startup_update.py'
 if not path.is_file():
  if not head: raise RuntimeError('Startup helper missing; run installer once')
  base='https://raw.githubusercontent.com/'+REPO+'/'+head+'/installer/'
  manifest=json.loads(fetch(base+'bootstrap.json'))
  digest=manifest.get('startup_files',{}).get('startup_update.py','')
  if not re.fullmatch('[a-f0-9]{64}',digest): raise ValueError('Missing verified startup helper')
  atomic(path,checked(base+'startup_update.py',digest))
 spec=importlib.util.spec_from_file_location('mastery_startup',path)
 module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
 module.bind(sys.modules[__name__]);return module

def main():
 global FETCH_TIMEOUT

 p=argparse.ArgumentParser(); p.add_argument('action',choices=['install','uninstall','startup','update']); p.add_argument('--startup',choices=['steam','manual','off']); p.add_argument('--target',type=Path); p.add_argument('--variant',choices=['Client','Server']); p.add_argument('--commit'); p.add_argument('--language',choices=['Ukrainian','English'])

 argv=sys.argv[1:]; tail=[]
 if '--' in argv:
  boundary=argv.index('--');tail=argv[boundary+1:];argv=argv[:boundary]
 a=p.parse_args(argv)
 if a.action=='update':FETCH_TIMEOUT=8
 if a.action in {'startup','update'} and not a.target:raise ValueError('Startup requires an explicit installed target')
 if a.variant is None:
  if a.target: a.variant='Server' if (a.target/'valheim_server.exe').exists() and not (a.target/'valheim.exe').exists() else 'Client'
  elif sys.platform=='darwin': a.variant='Client'
  else:
   answer=input('Install target: 1 Valheim game (default), 2 Dedicated server: ').strip()
   if answer not in {'','1','2'}: raise ValueError('Invalid target selection')
   a.variant='Server' if answer=='2' else 'Client'
 root=(a.target or select_target(a.variant)).absolute()
 print('Selected game folder:',root)

 identity(root,a.variant,interactive=a.action not in {'startup','update'})
 if a.action=='startup':return startup_module().run(root,tail)

 if busy(a.variant): raise RuntimeError('Close the selected Valheim client/server normally')

 if a.action=='uninstall': uninstall(root); return

 retire=legacy_candidates(root,a.variant)
 preferences_expected={}

 head=a.commit or json.loads(fetch('https://api.github.com/repos/'+REPO+'/commits/Release'))['sha']

 if not re.fullmatch('[0-9a-f]{40}',head): raise ValueError('Invalid release commit')
 if a.action=='update':
  recover_locked(root);current=load(root)
  if current and current.get('variant')!=a.variant:raise ValueError('Installed variant mismatch')
  if current and current.get('startup') and a.language is None and current.get('release_commit')==head:
   startup_module().ready_files(root)
   print('Already current; no mod/runtime downloads required:',head);return

 base='https://raw.githubusercontent.com/'+REPO+'/'+head+'/distribution/'

 manifest=json.loads(fetch(base+a.variant.lower()+'.json'))

 if manifest.get('schema')!=1 or manifest.get('product')!='ValheimMastery' or manifest.get('variant')!=a.variant: raise ValueError('Wrong mod manifest')

 if not re.fullmatch(r'[A-Za-z0-9_.-]+\.zip',manifest['archive']): raise ValueError('Unsafe archive name')

 payload=unpack(checked(base+manifest['archive'],manifest['archive_sha256']))

 if set(payload)!={r['path'] for r in manifest['files']}: raise ValueError('Mod file list mismatch')

 files={}

 for r in manifest['files']:

  n=r['path']

  if n!='ValheimMastery.dll' and n not in {'Localization/ukrainian.json','Localization/english.json','Localization/manifest.json'} and not n.startswith('assets/'): raise ValueError('Outside mod payload')

  if sha(payload[n])!=r['sha256'] or len(payload[n])!=r['size']: raise ValueError('Mod file checksum mismatch')

  files['BepInEx/plugins/ValheimMastery/'+n]=payload[n]

 if manifest.get('localization'):

  info=manifest['localization']

  if info.get('schema')!=1 or info.get('consumer')!='Localization.ModLanguage': raise ValueError('Unsupported language consumer')

  packs=info['packages']

  if {r['language'] for r in packs}!={'Ukrainian','English'} or len(packs)!=2: raise ValueError('Both reviewed language packs are required')

  keys=[]

  for row in packs:

   expected='ukrainian.json' if row['language']=='Ukrainian' else 'english.json'

   if row['file']!=expected: raise ValueError('Unsafe language package name')

   b=checked(base+'localization/'+expected,row['sha256']); pack=json.loads(b)

   if pack.get('schema')!=1 or pack.get('version')!=manifest['plugin_version'] or pack.get('language')!=row['language']: raise ValueError('Language package metadata mismatch')

   entries=pack['entries']; names=[r['key'] for r in entries]

   if len(names)!=len(set(names)) or not names or any(not re.fullmatch('vm_[a-z0-9_]+',n) for n in names): raise ValueError('Invalid language keys')

   if any(not isinstance(r['value'],str) or not r['value'] for r in entries): raise ValueError('Invalid language text')

   keys.append(set(names)); files['BepInEx/plugins/ValheimMastery/Localization/'+expected]=b

  if keys[0]!=keys[1]: raise ValueError('UA/EN language key coverage differs')

  import language_setting
  config='BepInEx/config/domestos.valheim.mastery.cfg'; path=safe(root,config)
  previous=path.read_bytes() if path.exists() else b''
  preferences_expected[config]=sha(previous) if path.exists() else None
  default=language_setting.current(previous) or 'Ukrainian'
  language=a.language
  if language is None and a.action=='update':language=default
  if language is None:
   answer=input('Mod language: 1 Ukrainian, 2 English; Enter keeps '+default+': ').strip()
   if answer not in {'','1','2'}: raise ValueError('Invalid language choice')
   language=default if not answer else ('English' if answer=='2' else 'Ukrainian')
  files[config]=language_setting.setting(previous,language)
  if info.get('manifest_sha256'):
   b=checked(base+'localization/manifest.json',info['manifest_sha256'])
   if json.loads(b).get('mod')!='domestos.valheim.mastery': raise ValueError('Wrong language manifest product')
   files['BepInEx/plugins/ValheimMastery/Localization/manifest.json']=b
 elif a.language: raise RuntimeError('This release has no reviewed language consumer; language selection is unavailable')
 name,h=BEP[sys.platform]

 dependency=checked('https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/'+name,h)

 recover_locked(root)

 old=load(root)

 if old and any(r['kind']=='framework' for r in old['files'].values()):

  # Managed framework may remain across updates; detect corruption through apply.

  known=unpack(dependency)

  files.update({n:known[n] for n,r in old['files'].items() if r['kind']=='framework' and n in known})

 else: files.update(framework(root,dependency))

 launch=None;external=None;startup=None
 helper=startup_module(head)
 launch,external,startup=helper.plan(root,old,a,head,files)
 apply(root,files,a.variant,manifest['version'],launch=launch,external=external,startup=startup,expected_files=preferences_expected,retire=retire,release_commit=head)
 if sys.platform=='darwin':
  for name in ('run_bepinex.sh','Launch-Mastery.command'):
   path=safe(root,name)
   if path.exists():path.chmod(path.stat().st_mode|0o100)

 print('Installed',manifest['version'],'from Release',head)

 print('Start Valheim normally through Steam. Game loading remains a player test.')

if __name__=='__main__':

 try: sys.exit(main() or 0)

 except Exception as e: print('Installer stopped:',e,file=sys.stderr); sys.exit(1)

