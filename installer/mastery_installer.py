#!/usr/bin/env python3

"""Valheim Mastery release installer. Standard library; no game launch."""

import argparse, hashlib, io, json, os, platform, re, shutil, struct, subprocess, sys, tempfile, urllib.request, uuid, zipfile

from pathlib import Path, PurePosixPath

REPO = 'Dimondomestos527/ValheimMastery'

MAX = 64*1024*1024

BEP = {

 'win32': ('BepInEx_win_x64_5.4.23.5.zip','82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'),

 'darwin': ('BepInEx_macos_universal_5.4.23.5.zip','01c2ae782eb016dfd6c345a18dbd2dcafffb3d9d318449d6486689f426b4a323')}

def sha(b): return hashlib.sha256(b).hexdigest()

def fetch(url):

 with urllib.request.urlopen(urllib.request.Request(url, headers={'User-Agent':'ValheimMastery-Installer/2'}),timeout=60) as r: b=r.read(MAX+1)

 if len(b)>MAX: raise ValueError('Download too large')

 return b

def checked(url, digest):

 b=fetch(url)

 if sha(b)!=digest.lower(): raise ValueError('Download checksum mismatch')

 return b

def steam_busy():

 if sys.platform!='darwin': return False

 return any(Path(line.strip()).name.casefold() in {'steam','steam_osx'} for line in subprocess.check_output(['ps','-axo','comm'],text=True).splitlines())

def allowed_config(path):

 path=Path(path).absolute()

 for steam in steam_roots():

  for config in (Path(steam)/'userdata').glob('*/config/localconfig.vdf'):

   if config.absolute()==path and not any(p.is_symlink() for p in [config,*config.parents]): return path

 raise ValueError('Outside recognized Steam user configuration')

def busy():

 if sys.platform=='win32':

  import csv

  names=[r[0].lower() for r in csv.reader(subprocess.check_output([str(Path(os.environ.get('SystemRoot','C:/Windows'))/'System32/tasklist.exe'),'/FO','CSV','/NH']).decode(errors='replace').splitlines()) if r]

 else: names=[Path(r.strip()).name.lower() for r in subprocess.check_output(['ps','-axo','comm'],text=True).splitlines()]

 return any(n in {'valheim','valheim.exe','valheim_server','valheim_server.exe'} for n in names)

def safe(root, name):

 p=PurePosixPath(name)

 if not name or '\\' in name or ':' in name or p.is_absolute() or '..' in p.parts or str(p)!=name: raise ValueError('Unsafe path')

 target=root/name

 for part in [target,*target.parents]:

  if part==root.parent: break

  if part.is_symlink() or (part.exists() and getattr(part.stat(),'st_file_attributes',0)&0x400): raise ValueError('Symlink/reparse path')

 target.resolve().relative_to(root.resolve())

 return target

def owned(name):

 return name=='BepInEx/config/domestos.valheim.mastery.cfg' or name in {'.doorstop_version','winhttp.dll','doorstop_config.ini','libdoorstop.dylib','run_bepinex.sh','Launch-Mastery.command'} or name.startswith('BepInEx/core/') or name.startswith('BepInEx/plugins/ValheimMastery/')

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

  if not owned(n) or not re.fullmatch('[0-9a-f]{64}',r.get('installed','')): raise ValueError('Invalid owned entry')

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

 if steam_busy(): raise RuntimeError('Close Steam normally before configuration recovery')

 control=safe(root,'.mastery-installer')

 if not control.exists(): return

 lock=InstallerLock(root); lock.acquire()

 try: recover(root)

 finally: lock.close()

def transaction(root, changes, state, inject=None, expected=None, external=None):

 if busy() or (sys.platform=='darwin' and steam_busy()): raise RuntimeError('Close Valheim/server and, on Mac, Steam normally before continuing')

 control=safe(root,'.mastery-installer'); control.mkdir(exist_ok=True)

 lock=InstallerLock(root); lock.acquire()

 try:

  recover(root)

  if load(root)!=expected: raise RuntimeError('Ownership changed during preparation; retry')

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

  if busy(): raise RuntimeError('Game started during staging')

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

def apply(root, files, variant, version, inject=None, launch=None, external=None):

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

   records[n]={'installed':sha(b),'original':original,'kind':'preference' if n=='BepInEx/config/domestos.valheim.mastery.cfg' else ('mod' if n.startswith('BepInEx/plugins/ValheimMastery/') else 'framework')}

 changes=dict(files)

 for n in list(records):

  if records[n]['kind']=='mod' and n not in files:

   p=safe(root,n)

   if not p.is_file() or sha(p.read_bytes())!=records[n]['installed']: raise ValueError('Stale owned file changed')

   original=records[n]['original']

   changes[n]=safe(root,'.mastery-installer/objects/'+original).read_bytes() if original else None

   if original and sha(changes[n])!=original: raise ValueError('Original backup corrupt')

   del records[n]

 state={'schema':2,'product':'ValheimMastery','variant':variant,'version':version,'files':records}

 if launch is not None: state['mac_launch']=launch

 elif old and old.get('mac_launch'): state['mac_launch']=old['mac_launch']

 transaction(root,changes,state,inject,expected=old,external=external)

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

  if r['kind']=='mod' and n.lower().endswith('.dll'):

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

 external=None; launch=old.get('mac_launch')

 if launch and not foreign:

  import steam_launch

  config=allowed_config(launch['path']); b=config.read_bytes(); text=b.decode('utf-8')

  if steam_launch.launch_value(text)[0]==launch['installed']:

   external=(config,steam_launch.set_launch(text,launch['original']).encode('utf-8'),sha(b))

  else: print('Preserved user-changed Steam launch options')

 transaction(root,changes,dict(old,files=keep,version='uninstalled'),inject,expected=old,external=external)

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

def identity(root,variant):

 if sys.platform=='win32':

  exe=safe(root,'valheim.exe' if variant=='Client' else 'valheim_server.exe')

  with exe.open('rb') as f:

   if f.read(2)!=b'MZ': raise ValueError('Not a Windows executable')

   f.seek(0x3c); offset=struct.unpack('<I',f.read(4))[0]; f.seek(offset)

   if f.read(4)!=b'PE\0\0' or f.read(2)!=b'\x64\x86': raise ValueError('Requires Windows x64 Valheim')

 else:

  import plistlib

  apps=list(root.glob('*.app'))

  if variant!='Client' or len(apps)!=1: raise ValueError('Mac requires one native Valheim.app client')

  app=apps[0]

  if app.stem.casefold()!='valheim': raise ValueError('Not Valheim.app')

  with (app/'Contents/Info.plist').open('rb') as f: info=plistlib.load(f)

  exe=app/'Contents/MacOS'/info['CFBundleExecutable']

  description=subprocess.check_output(['/usr/bin/file','-b',str(exe)],text=True)

  if 'x86_64' not in description: raise ValueError('Current BepInEx5 path requires x86_64 game slice')

  if platform.machine()=='arm64' and subprocess.run(['/usr/bin/arch','-x86_64','/usr/bin/true']).returncode:

   answer=input('Rosetta is required. Start the Apple installer now? You must accept its license yourself. [y/N] ').strip().lower()

   if answer!='y' or subprocess.run(['/usr/sbin/softwareupdate','--install-rosetta']).returncode: raise RuntimeError('Rosetta was not installed')

   if subprocess.run(['/usr/bin/arch','-x86_64','/usr/bin/true']).returncode: raise RuntimeError('Rosetta validation failed')

def framework(root,blob):

 expected={n:b for n,b in unpack(blob).items() if owned(n)}

 markers=['BepInEx/core','winhttp.dll','doorstop_config.ini','.doorstop_version','libdoorstop.dylib','run_bepinex.sh']

 core=safe(root,'BepInEx/core')
 # Uninstall preserves directories/configs: an empty core folder is not a framework.
 core_present=core.exists() and (not core.is_dir() or any(core.iterdir()))
 any_existing=core_present or any(safe(root,n).exists() for n in markers if n!='BepInEx/core')

 if any_existing:

  for n,b in expected.items():

   p=safe(root,n)

   if not p.is_file() or sha(p.read_bytes())!=sha(b): raise ValueError('Existing BepInEx differs or is incomplete; preserved without overwriting: '+n)

  return {} # Exactly compatible preexisting framework is never adopted.

 return expected

def main():

 p=argparse.ArgumentParser(); p.add_argument('action',choices=['install','uninstall']); p.add_argument('--target',type=Path); p.add_argument('--variant',choices=['Client','Server']); p.add_argument('--commit'); p.add_argument('--language',choices=['Ukrainian','English'])

 a=p.parse_args()
 if a.variant is None:
  if a.target: a.variant='Server' if (a.target/'valheim_server.exe').exists() and not (a.target/'valheim.exe').exists() else 'Client'
  elif sys.platform=='darwin': a.variant='Client'
  else:
   answer=input('Install target: 1 Valheim game (default), 2 Dedicated server: ').strip()
   if answer not in {'','1','2'}: raise ValueError('Invalid target selection')
   a.variant='Server' if answer=='2' else 'Client'
 root=(a.target or select_target(a.variant)).absolute()
 print('Selected game folder:',root)

 identity(root,a.variant)

 if busy(): raise RuntimeError('Close Valheim and its server normally')

 if a.action=='uninstall': uninstall(root); return

 head=a.commit or json.loads(fetch('https://api.github.com/repos/'+REPO+'/commits/Release'))['sha']

 if not re.fullmatch('[0-9a-f]{40}',head): raise ValueError('Invalid release commit')

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
  default=language_setting.current(previous) or 'Ukrainian'
  language=a.language
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

 launch=None; external=None

 if sys.platform=='darwin':

  import steam_launch

  if steam_busy(): raise RuntimeError('Close Steam normally, then run installer again')

  configs=[p for steam in steam_roots() for p in (Path(steam)/'userdata').glob('*/config/localconfig.vdf') if p.is_file()]

  valid=[]

  for config in configs:

   try: steam_launch.launch_value(config.read_text(encoding='utf-8')); valid.append(config)

   except ValueError: pass

  if not valid: raise RuntimeError('No Steam profile with Valheim found. Run vanilla once, exit game and Steam, then retry.')

  config=allowed_config(choose(valid)); b=config.read_bytes(); text=b.decode('utf-8'); current=steam_launch.launch_value(text)[0]

  if old and old.get('mac_launch'):

   launch=old['mac_launch']

   if str(config)!=launch['path'] or current!=launch['installed']: raise RuntimeError('Steam launch setting changed; preserved for review')

  else:

   wanted=steam_launch.desired(root,current,platform.machine()=='arm64')

   launch={'path':str(config),'original':current,'installed':wanted}; external=(config,steam_launch.set_launch(text,wanted).encode('utf-8'),sha(b))

 apply(root,files,a.variant,manifest['version'],launch=launch,external=external)

 if sys.platform=='darwin':

  safe(root,'run_bepinex.sh').chmod(safe(root,'run_bepinex.sh').stat().st_mode|0o100)

 print('Installed',manifest['version'],'from Release',head)

 print('Start Valheim normally through Steam. Game loading remains a player test.')

if __name__=='__main__':

 try: main()

 except Exception as e: print('Installer stopped:',e,file=sys.stderr); sys.exit(1)

