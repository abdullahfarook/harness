#Requires -Version 7.0
[CmdletBinding()]
param([string]$KubeConfig=(Join-Path (Split-Path $PSScriptRoot -Parent) 'kube.config'), [string]$Context, [string]$Namespace='hermes', [string]$Release='hermes')
. "$(Split-Path $PSScriptRoot -Parent)/scripts/Common.ps1"
Initialize-Cluster $KubeConfig $Context
$files=Join-Path (Split-Path $PSScriptRoot -Parent) 'helm/hermes-stack/files'
$sources=@{}
foreach ($name in 'download','prepare_hermes','smoke') { $sources[$name]=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((Get-Content "$files/$name.py" -Raw))) }
$code=@'
import base64, json, os, tempfile, unittest
from pathlib import Path
from unittest.mock import Mock, patch
import urllib.error
os.environ.update(LLM_URL='http://test',HERMES_URL='http://agent',UI_URL='http://ui',API_SERVER_KEY='test-key')
sources=json.loads('SOURCES_PLACEHOLDER')
modules={}
for name, source in sources.items():
    space={'__name__':'unit_'+name}
    exec(compile(base64.b64decode(source),name+'.py','exec'),space)
    modules[name]=space
class Tests(unittest.TestCase):
    def test_config_merge_preserves_unmanaged(self):
        a={'model':{'default':'old','custom_setting':7},'memory':{'enabled':True}}
        b={'model':{'default':'new'}}
        modules['prepare_hermes']['merge'](a,b)
        self.assertEqual(a,{'model':{'default':'new','custom_setting':7},'memory':{'enabled':True}})
    def test_config_merge_replaces_managed_lists(self):
        a={'toolsets':['old']}; modules['prepare_hermes']['merge'](a,{'toolsets':['terminal']})
        self.assertEqual(a['toolsets'],['terminal'])
    def test_checksum(self):
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'data';p.write_bytes(b'abc')
            self.assertEqual(modules['download']['digest'](p),'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')
    def test_cache_skip(self):
        m=modules['download']
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'model.gguf';p.write_bytes(b'abc')
            with patch.dict(os.environ,MODEL_FILE=p.name,MODEL_SHA256=m['digest'](p)),patch.dict(m,Path=lambda _:Path(d)),patch.object(m['urllib'].request,'urlopen') as network:
                m['download']();network.assert_not_called()
    def test_patch_is_narrow(self):
        m=modules['prepare_hermes']; source=Mock();source.read_text.return_value='before=1\nMINIMUM_CONTEXT_LENGTH = 64_000\nafter=2\n';target=Mock()
        with patch.dict(os.environ,SMALL_CONTEXT_LENGTH='8192'),patch.dict(m,Path=lambda p:source if p.startswith('/opt/hermes') else target): m['patch']()
        target.write_text.assert_called_once_with('before=1\nMINIMUM_CONTEXT_LENGTH = 8192\nafter=2\n')
    def test_patch_rejects_changed_upstream(self):
        m=modules['prepare_hermes'];source=Mock();source.read_text.return_value='MINIMUM_CONTEXT_LENGTH = 32000'
        with patch.dict(m,Path=lambda _:source),self.assertRaises(RuntimeError):m['patch']()
    def test_patch_rejects_duplicate_guard(self):
        m=modules['prepare_hermes'];source=Mock();source.read_text.return_value='MINIMUM_CONTEXT_LENGTH = 64_000\n'*2
        with patch.dict(m,Path=lambda _:source),self.assertRaises(RuntimeError):m['patch']()
    def test_patch_rejects_out_of_range(self):
        m=modules['prepare_hermes'];source=Mock();source.read_text.return_value='MINIMUM_CONTEXT_LENGTH = 64_000'
        with patch.dict(os.environ,SMALL_CONTEXT_LENGTH='64000'),patch.dict(m,Path=lambda _:source),self.assertRaises(ValueError):m['patch']()
    def test_auth_must_reject(self):
        m=modules['smoke']
        with patch.dict(m,request=lambda *a,**k:{}),self.assertRaises(AssertionError):m['auth_rejection']()
    def test_auth_rejects_both_keys(self):
        m=modules['smoke'];req=Mock(side_effect=urllib.error.HTTPError('http://test',401,'unauthorized',{},None))
        with patch.dict(m,request=req):m['auth_rejection']()
        self.assertEqual(req.call_count,2)
    def test_empty_chat_cannot_pass(self):
        m=modules['smoke']
        with patch.dict(m,request=lambda *a,**k:{'choices':[{'message':{'content':''}}]}),self.assertRaises(AssertionError):m['completion']('http://test','model')
    def test_claimed_tool_execution_cannot_pass(self):
        m=modules['smoke']
        with patch.dict(m,request=lambda *a,**k:{'output':[{'type':'message','content':'Done'}]}),self.assertRaises(AssertionError):m['tool_execution']()
unittest.main(verbosity=2)
'@
$code=$code.Replace('SOURCES_PLACEHOLDER',($sources | ConvertTo-Json -Compress))
$r=Invoke-Kube -Arguments @('exec','-i','-n',$Namespace,"statefulset/$Release-hermes",'--','python','-') -InputText $code
Write-Host $r.Output
Write-Host $r.Error
