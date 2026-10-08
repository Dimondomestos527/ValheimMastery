"""Pre-launch barrier and initial setup. No polling, no shell-built game command."""
import hashlib,io,json,os,platform,re,shutil,subprocess,sys,tarfile,time,uuid
from pathlib import Path

def bind(core):
 global m
 m=core

PYTHON={
 'win32':('x86_64-pc-windows-msvc','5e100ee3d592ff500f4408a624f054d202e32d9dba8a12b2226bef81083fd778'),
 'arm64':('aarch64-apple-darwin','d8975d7df4f08f7b1c7aafcdfacbddcec3d366415f2c1a72b2466b6850815933'),
 'x86_64':('x86_64-apple-darwin','8e9cb087305bfb8969f68a905f79f41469d4aa5220c1aa71ada7fc9953bdba0f')}
HELPERS=('mastery_installer.py','steam_launch.py','language_setting.py','startup_update.py')

def log(root,message):
 print('Mastery:',message,flush=True)
 path=m.safe(root,'.mastery-installer/startup.log')
 if path.exists() and path.stat().st_size>1024*1024:
  m.atomic(path,path.read_bytes()[-128*1024:])
 with path.open('a',encoding='utf-8') as out:out.write(time.strftime('%Y-%m-%d %H:%M:%S')+' '+message+'\n')

def python_relative():
 return '.mastery-installer/runtime/python/'+('python.exe' if sys.platform=='win32' else 'bin/python3.13')

def ensure_runtime(root):
 target=m.safe(root,'.mastery-installer/runtime')
 marker=m.safe(root,'.mastery-installer/runtime/verified.json')
 key='win32' if sys.platform=='win32' else platform.machine()
 name,digest=PYTHON[key]
 interpreter=m.safe(root,python_relative())
 if marker.exists():
  record=json.loads(marker.read_bytes())
  if record.get('archive_sha256')!=digest or not interpreter.is_file():raise ValueError('Private runtime changed; rerun installer for review')
  if m.sha(interpreter.read_bytes())!=record['interpreter_sha256']:raise ValueError('Private interpreter checksum changed')
  return interpreter
 if target.exists():raise ValueError('Incomplete private runtime preserved; select a clean target or review runtime cache')
 candidates=[Path(os.environ['VM_PYTHON_ARCHIVE'])] if os.environ.get('VM_PYTHON_ARCHIVE') else []
 candidates.append(Path(m.__file__).parent/'python.tar.gz')
 if sys.platform=='win32':candidates.append(Path(os.environ.get('LOCALAPPDATA',''))/'ValheimMasteryInstaller/python-3.13.16-20261003-x64.tar.gz')
 blob=None
 for candidate in candidates:
  if candidate.is_file() and not candidate.is_symlink():
   data=candidate.read_bytes()
   if m.sha(data)==digest:blob=data;break
 if blob is None:
  blob=m.checked('https://github.com/astral-sh/python-build-standalone/releases/download/20261003/cpython-3.13.16%2B20261003-'+name+'-install_only.tar.gz',digest)
 stage=m.safe(root,'.mastery-installer/runtime-stage-'+uuid.uuid4().hex)
 stage.mkdir(parents=True)
 # CPython's internal relative symlinks are allowed by tarfile's data filter;
 # absolute/escaping links and special-device entries are refused.
 with tarfile.open(fileobj=io.BytesIO(blob),mode='r:gz') as archive:
  if sum(member.size for member in archive.getmembers())>512*1024*1024:raise ValueError('Private runtime too large')
  archive.extractall(stage,filter='data')
 exe=stage/'python'/('python.exe' if sys.platform=='win32' else 'bin/python3.13')
 if not exe.is_file():raise ValueError('Invalid private runtime layout')
 (stage/'verified.json').write_text(json.dumps({'archive_sha256':digest,'interpreter_sha256':m.sha(exe.read_bytes())})+'\n',encoding='utf-8')
 # Cache directories are outside the mod-ownership transaction; publishing only
 # a completely verified directory avoids a half-installed launcher dependency.
 os.replace(stage,target)
 return interpreter

def remote_helpers(head):
 base='https://raw.githubusercontent.com/'+m.REPO+'/'+head+'/installer/'
 manifest=json.loads(m.fetch(base+'bootstrap.json'))
 if manifest.get('schema')!=2:raise ValueError('Invalid updater bootstrap manifest')
 digests=dict(manifest['runtime_files']);digests.update(manifest.get('startup_files',{}))
 result={}
 for name in (*HELPERS,'Launch-Mastery.exe'):
  if name=='Launch-Mastery.exe' and sys.platform!='win32':continue
  digest=digests.get(name,'')
  if not re.fullmatch('[a-f0-9]{64}',digest):raise ValueError('Missing verified startup file: '+name)
  result[name]=m.checked(base+name,digest)
 return result

def desired_startup(root,original):
 original=original or ''
 if '%command%' in original.casefold() or 'bepinex' in original.casefold():raise ValueError('Existing external launcher preserved; choose manual mode or review it')
 script=str(root/('Launch-Mastery.exe' if sys.platform=='win32' else 'Launch-Mastery.command'))
 if any(c in script for c in '\r\n\0"'):raise ValueError('Unsupported quoted/newline game path')
 if sys.platform=='darwin':script=script.replace('\\','\\\\').replace('$','\\$').replace('`','\\`')
 return '"'+script+'" %command%'+(' '+original if original else '')

def plan(root,old,args,head,files):
 import steam_launch as st
 previous=(old or {}).get('steam_launch') or (old or {}).get('mac_launch')
 prior=(old or {}).get('startup')
 mode=args.startup or (prior or {}).get('mode') or ('steam' if args.variant=='Client' else 'off')
 if args.action=='update':mode=(prior or {}).get('mode','off')
 roots=m.steam_roots()
 is_steam=any(root.resolve()==p.resolve() for p in m.discover(roots,args.variant))
 # A manually selected separate dev/store copy must never hijack Steam's client.
 if mode=='steam' and not is_steam:mode='manual'
 launch=previous;external=None
 if mode!='off':
  if args.variant!='Client':raise ValueError('Automatic launcher currently targets the game client only')
  if args.action!='update':ensure_runtime(root)
  elif not m.safe(root,python_relative()).is_file():raise ValueError('Private launcher runtime missing; rerun installer')
  helpers=remote_helpers(head)
  for name in HELPERS:files['.mastery-installer/updater/'+name]=helpers[name]
  if sys.platform=='win32':
   launcher='Launch-Mastery.exe'
   # Windows cannot replace the native launcher currently running. Its next
   # manual installer run can upgrade it; mod/helper updates still proceed.
   if args.action=='update' and m.safe(root,launcher).exists():files[launcher]=m.safe(root,launcher).read_bytes()
   else:files[launcher]=helpers[launcher]
  else:
   files['Launch-Mastery.command']=b'#!/bin/bash\nset -euo pipefail\nroot="$(cd -- "$(dirname -- "$0")" && pwd -P)"\nexec "$root/.mastery-installer/runtime/python/bin/python3.13" -X utf8 "$root/.mastery-installer/updater/mastery_installer.py" startup --target "$root" --variant Client -- "$@"\n'
  startup={'schema':1,'mode':mode,'timeout_seconds':45,'runtime':python_relative()}
 else:startup=False
 if args.action=='update':return launch,None,startup
 # Configure only the exact game's known Steam profile, only while Steam is shut.
 need_steam=(mode=='steam' or (sys.platform=='darwin' and is_steam))
 if need_steam or previous:
  if m.steam_busy():raise RuntimeError('Close Steam normally, then rerun installer once to configure launch options')
  if previous:
   config=m.allowed_config(previous['path'])
  else:
   configs=[]
   for steam in roots:
    for candidate in (steam/'userdata').glob('*/config/localconfig.vdf'):
     if not candidate.is_file():continue
     try:st.launch_value(candidate.read_text(encoding='utf-8'));configs.append(candidate)
     except ValueError:pass
   if not configs:raise RuntimeError('Run vanilla once, exit Valheim and Steam, then retry; no Valheim Steam profile was found')
   config=m.allowed_config(m.choose(configs))
  before=config.read_bytes();current=st.launch_value(before.decode('utf-8'))[0]
  if previous and current!=previous['installed']:raise ValueError('Steam launch options changed; preserved for review')
  original=previous['original'] if previous else current
  fallback=st.desired(root,original,platform.machine()=='arm64') if sys.platform=='darwin' else original
  wanted=desired_startup(root,original) if mode=='steam' else fallback
  launch={'path':str(config),'original':original,'installed':wanted,'fallback':fallback,'startup':mode=='steam'}
  if wanted!=current:external=(config,st.set_launch(before.decode('utf-8'),wanted).encode('utf-8'),m.sha(before))
 if mode=='manual':print('Automatic updates are enabled through Launch-Mastery; this separate folder did not change Steam settings.')
 return launch,external,startup

def ready_files(root):
 state=m.load(root)
 if not state or state.get('version')=='uninstalled' or not state.get('startup'):raise ValueError('No installed auto-update configuration; run installer once')
 for name,row in state['files'].items():
  if row['kind']=='preference':continue
  path=m.safe(root,name)
  if row['kind']=='retired':
   if path.exists():raise ValueError('Retired duplicate DLL reappeared: '+name)
   continue
  if not path.is_file() or m.sha(path.read_bytes())!=row['installed']:raise ValueError('Installed file integrity check failed: '+name)
 if not m.safe(root,'BepInEx/plugins/ValheimMastery/ValheimMastery.dll').is_file():raise ValueError('Mastery DLL is missing')
 if m.legacy_candidates(root,state['variant']):raise ValueError('Legacy duplicate exists; rerun the installer first')
 marker=json.loads(m.safe(root,'.mastery-installer/runtime/verified.json').read_bytes())
 interpreter=m.safe(root,state['startup']['runtime'])
 if not interpreter.is_file() or m.sha(interpreter.read_bytes())!=marker['interpreter_sha256']:raise ValueError('Private interpreter changed')
 return state

def game_command(root,args):
 if sys.platform=='win32':expected=m.safe(root,'valheim.exe')
 else:
  expected,app=m.mac_executable(root)
 if not args:args=[str(expected)]
 first=Path(args[0])
 if not first.is_absolute():first=root/first
 allowed={expected.resolve()}
 if sys.platform=='darwin':allowed.add(app.resolve())
 if first.resolve() not in allowed:raise ValueError('Steam launch did not provide this folder\'s Valheim executable')
 if sys.platform=='darwin':
  return (['/usr/bin/arch','-x86_64'] if platform.machine()=='arm64' else [])+[str(m.safe(root,'run_bepinex.sh')),str(first),*args[1:]]
 return [str(expected),*args[1:]]

def run(root,argv):
 root=Path(root).absolute()
 if m.busy('Client'):raise RuntimeError('Valheim client is already running; no loaded DLL is updated')
 command=game_command(root,argv)
 m.recover_locked(root)
 state=ready_files(root)
 interpreter=m.safe(root,state['startup']['runtime'])
 core=m.safe(root,'.mastery-installer/updater/mastery_installer.py')
 log(root,'Checking Release before game launch')
 try:
  result=subprocess.run([str(interpreter),'-X','utf8',str(core),'update','--target',str(root),'--variant','Client'],cwd=root,timeout=state['startup']['timeout_seconds'])
  if result.returncode:log(root,'Update unavailable or rejected; keeping installed files')
 except subprocess.TimeoutExpired:
  log(root,'Update timed out; recovering the previous complete transaction')
 except OSError as exc:
  log(root,'Update worker unavailable; keeping installed files: '+str(exc))
 # Worker is gone after timeout; recover any pending journal before allowing game.
 m.recover_locked(root)
 lock=m.InstallerLock(root);lock.acquire()
 try:
  m.recover(root);state=ready_files(root)
  if m.busy('Client'):raise RuntimeError('Another Valheim client started; launch cancelled')
  log(root,'Starting Valheim with '+state['version'])
  child=subprocess.Popen(command,cwd=root,shell=False)
  # Hold the same lock through game lifetime to serialize simultaneous launchers
  # and prevent even a second installer from racing the game's startup window.
  return child.wait()
 finally:lock.close()
