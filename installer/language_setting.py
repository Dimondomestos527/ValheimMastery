"""Installer language setting, preserving unrelated BepInEx configuration."""
import re

def setting(raw,language):
 if language not in {'Ukrainian','English'}: raise ValueError('Unsupported mod language')
 bom=raw.startswith(b'\xef\xbb\xbf'); text=raw.decode('utf-8-sig'); newline='\r\n' if '\r\n' in text else '\n'
 lines=text.splitlines(keepends=True); starts=[]
 for i,line in enumerate(lines):
  if re.fullmatch(r'\s*\[Localization\]\s*',line.strip()): starts.append(i)
 if len(starts)>1: raise ValueError('Duplicate Localization configuration section')
 if not starts:
  text=text.rstrip('\r\n')+newline+newline+'[Localization]'+newline+'ModLanguage = '+language+newline
 else:
  start=starts[0]; end=next((i for i in range(start+1,len(lines)) if re.match(r'^\s*\[',lines[i])),len(lines))
  keys=[i for i in range(start+1,end) if re.match(r'^\s*ModLanguage\s*=',lines[i])]
  if len(keys)>1: raise ValueError('Duplicate ModLanguage configuration key')
  if keys: lines[keys[0]]='ModLanguage = '+language+newline
  else: lines.insert(start+1,'ModLanguage = '+language+newline)
  text=''.join(lines)
 return (b'\xef\xbb\xbf' if bom else b'')+text.encode('utf-8')

def current(raw):
 section=None; selected=None; sections=0; keys=0
 for line in raw.decode('utf-8-sig').splitlines():
  match=re.fullmatch(r'\s*\[([^]]+)\]\s*',line)
  if match:
   section=match.group(1)
   if section=='Localization': sections+=1
  elif section=='Localization':
   match=re.match(r'^\s*ModLanguage\s*=\s*(.*?)\s*$',line)
   if match: keys+=1; selected=match.group(1)
 if sections>1 or keys>1: raise ValueError('Duplicate language configuration')
 return selected if selected in {'Ukrainian','English'} else None
