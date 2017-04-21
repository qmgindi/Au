 /
function# $action ARRAY(POSTFIELD)&a [str&responsepage] [$headers] [fa] [fparam] [inetflags] [str&responseheaders]

 Posts web form data.
 Returns 1 if successful, 0 if fails.
 Error if a field name is empty or a file cannot be opened.
 At first call Connect to connect to web server.

 action - script's path relative to server. See "action" field in form's HTML. Example "forum\login.php".
 a - array containing name-value pairs. POSTFIELD members:
   name - field name. Same as "name" field in form's HTML.
   value - field value. Same as "value" field in form's HTML. If it is file field, must specify file.
   isfile - must be 0 for simple input fields, 1 for file fields.
 responsepage - receives response page (HTML).
 headers - additional headers.
 fa - address of callback function that will be called repeatedly while sending data. Can be used to show progress.
   function# action nbAll nbWritten $_file nbAllF nbWrittenF fparam
   Arguments:
     action - 0 starting to send data, 1 sending file, 2 all data sent, 3 starting to receive response, 4 finished.
     nbAll - total size of data being uploaded.
     nbWritten - size of already uploaded part of total.
     _file - current file.
     nbAllF - size of current file.
     nbWrittenF - size of already uploaded part of current file.
   Return value: 0 continue, 1 cancel.
   Example: search in forum.
 fparam - some value to pass to the callback function.
 inetflags (QM 2.2.1) - a combination of INTERNET_FLAG_x flags. Documented in the MSDN library on the internet. For example, use INTERNET_FLAG_NO_AUTO_REDIRECT to disable redirection. Flag INTERNET_FLAG_RELOAD is always added.
 responseheaders (QM 2.2.1) - receives raw response headers.

 EXAMPLE
 ARRAY(POSTFIELD) a.create(2)
 a[0].name="testtxt"; a[0].value="some text"; a[0].isfile=0
 a[1].name="testfile"; a[1].value="$desktop$\test.gif"; a[1].isfile=1
 Http h.Connect("www.xxx.com"); str r
 if(!h.PostFormData("form.php" a r)) end "failed"
 out r


type ___POSTFILE index __HFile'hfile size str'sf str'sh
type ___POSTPROGRESS fa fparam nbtotal wrtotal

int i size size2 bufsize(4096) wrfile
str s s1 s2 sb bound sh buf
ARRAY(___POSTFILE) af
POSTFIELD& p; ___POSTFILE& f; ___POSTPROGRESS pp

for(i 0 a.len)
	if(!a[i].name.len) end "field name is empty"
	if(a[i].isfile) af[].index=i

if(!af.len and !fa)
	 urlencode, call Post, return
	for i 0 a.len
		s1=a[i].name; s2=a[i].value
		s.formata("%s%s=%s" iif(i "&" "") s1.escape(9) s2.escape(9))
	ret Post(action s responsepage headers inetflags responseheaders)

 set headers
INTERNET_BUFFERS b.dwStructSize=sizeof(b)
bound="[]--7d23542a1a12c2"
b.lpcszHeader="Content-Type: multipart/form-data; boundary=7d23542a1a12c2"
if(len(headers)) s=headers; s.trim("[]"); b.lpcszHeader=sh.from(s "[]" b.lpcszHeader)
b.dwHeadersLength=len(b.lpcszHeader)

 format non-file fields and store into b
for i 0 a.len
	&p=a[i]
	if(p.isfile) continue
	sb.formata("%s[]Content-Disposition: form-data; name=''%s''[][]%s" bound+iif(i 0 2) p.name p.value)
b.lpvBuffer=sb
b.dwBufferLength=sb.len
b.dwBufferTotal=sb.len+bound.len+4

 open files, format headers, calculate total data size
for i 0 af.len
	&f=af[i]; &p=a[f.index]
	f.sf.searchpath(p.value)
	if(!GetFileContentType(f.sf s2)) s2="text/plain"
	f.hfile.Create(f.sf OPEN_EXISTING GENERIC_READ FILE_SHARE_READ); err end _error
	f.size=GetFileSize(f.hfile 0)
	b.dwBufferTotal+f.size
	f.sh.format("%s[]Content-Disposition: form-data; name=''%s''; filename=''%s''[]Content-Type: %s[][]" bound+iif(i||sb.len 0 2) p.name f.sf s2)
	b.dwBufferTotal+f.sh.len

 init progress
pp.fa=fa; pp.fparam=fparam; pp.nbtotal=b.dwBufferTotal
if(fa && PostProgress(0 pp)) ret

 open request, send non-file fields
__HInternet hi=HttpOpenRequest(m_hi "POST" action 0 0 0 INTERNET_FLAG_RELOAD|inetflags 0); if(!hi) goto e
if(!HttpSendRequestEx(hi &b 0 0 0)) goto e
pp.wrtotal=sb.len

 send files
if(af.len) buf.all(bufsize)
for i 0 af.len
	&f=af[i]
	if(fa && PostProgress(1 pp f.sf f.size)) ret
	if(!InternetWriteFile(hi f.sh f.sh.len &size2)) goto e
	for wrfile 0 f.size 0
		if(!ReadFile(f.hfile buf bufsize &size 0)) ret
		if(!InternetWriteFile(hi buf size &size2)) goto e
		wrfile+size2; pp.wrtotal+size2
		if(fa && PostProgress(1 pp f.sf f.size wrfile)) ret
	f.hfile.Close

 write last boundary, end request
bound+"--[]"
if(!InternetWriteFile(hi bound bound.len &size2) or size2!=bound.len) goto e
if(!HttpEndRequest(hi 0 0 0)) goto e
if(fa && PostProgress(2 pp)) ret

 get response headers
if(&responseheaders and !GetResponseHeaders(hi responseheaders)) goto e

 read response
if(&responsepage)
	if(fa && PostProgress(3 pp)) ret
	if(!Read(hi responsepage)) ret
if(fa && PostProgress(4 pp)) ret
ret 1
 e
Error