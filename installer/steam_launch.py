"""Bounded Steam LaunchOptions editor. Never launches Steam or Valheim."""
import re
from pathlib import Path

def tokens(text):
 pattern=r'//[^\n]*|"(?:\\.|[^"\\])*"|[{}]|[^\s{}"]+'
 return [m for m in re.finditer(pattern,text) if not m.group().startswith('//')]
def decode(t):
 return re.sub(r'\\([\\"])',r'\1',t[1:-1]) if t.startswith('"') else t
def quote(value):
 if '\n' in value or '\r' in value or '\0' in value: raise ValueError('Invalid launch value')
 return '"'+value.replace('\\','\\\\').replace('"','\\"')+'"'
def app_span(text):
 ts=tokens(text); position=0; matches=[]
 def parse(path=()):
  nonlocal position
  keys=set()
  while position<len(ts):
   key_token=ts[position]; position+=1
   if key_token.group()=='}': return key_token
   key=decode(key_token.group())
   if key in keys or key in {'{','}'} or position>=len(ts): raise ValueError('Invalid Steam VDF')
   keys.add(key); value=ts[position]; position+=1
   if value.group()=='{':
    closing=parse(path+(key,))
    if tuple(x.casefold() for x in path+(key,))[-4:]==('software','valve','steam','apps'):
     pass
    if key=='892970' and tuple(x.casefold() for x in path[-4:])==('software','valve','steam','apps'):
     matches.append((value.end(),closing.start()))
   elif value.group()=='}': raise ValueError('Missing VDF value')
  if path: raise ValueError('Unclosed Steam VDF')
 parse()
 if len(matches)!=1: raise ValueError('Steam localconfig must contain exactly one Valheim app entry')
 return matches[0]
def launch_value(text):
 begin,end=app_span(text); ts=tokens(text[begin:end]); depth=0; found=[]
 for i,t in enumerate(ts):
  if t.group()=='{': depth+=1
  elif t.group()=='}': depth-=1
  elif depth==0 and decode(t.group()).casefold()=='launchoptions':
   if i+1>=len(ts) or ts[i+1].group() in {'{','}'}: raise ValueError('Invalid LaunchOptions')
   found.append(ts[i+1])
 if len(found)>1: raise ValueError('Duplicate LaunchOptions')
 if found:
  t=found[0]; return decode(t.group()),(begin+t.start(),begin+t.end())
 return None,(end,end)
def set_launch(text,value):
 current,(start,end)=launch_value(text)
 if current is None:
  if value is None: return text
  return text[:start]+'\n\t\t\t\t\t"LaunchOptions"\t\t'+quote(value)+'\n'+text[end:]
 if value is None:
  # Remove only the exact property pair, leaving unrelated configuration untouched.
  ts=tokens(text); before=[t for t in ts if t.end()<=start]; key=before[-1]
  if decode(key.group()).casefold()!='launchoptions': raise ValueError('Invalid property boundary')
  return text[:key.start()]+text[end:]
 return text[:start]+quote(value)+text[end:]
def desired(root,original,arm):
 original=original or ''
 if '%command%' in original or 'bepinex' in original.casefold(): raise ValueError('Existing Steam launch wrapper requires manual review; preserved')
 script=str(Path(root)/'run_bepinex.sh')
 if any(c in script for c in '\n\r\0'): raise ValueError('Invalid game path')
 script='"'+script.replace('\\','\\\\').replace('"','\\"').replace('$','\\$').replace('`','\\`')+'"'
 return ('/usr/bin/arch -x86_64 ' if arm else '')+script+' %command%'+(' '+original if original else '')
