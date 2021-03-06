//This small C++ program is used to run a script in Aedit.exe through command line.
//Aedit.exe itself does not support it through its command line directly, because:
//	1. Its process starts slowly because is a .NET app.
//	2. If the caller process waits until script ends, it would have to wait until Aedit.exe exits.
//Also this program is used to run Aedit as admin without UAC consent.

#include "stdafx.h"
#include "Au.CL.h"

//These 2 functions are used to run Aedit as admin without UAC consent.
//1. Run this program with command line "/e".
//2. It calls _RunEditorAsAdminE, which starts a scheduled task that runs this program with command line "/s".
//3. The task is set to run as SYSTEM. It runs this program with command line "/s".
//4. It calls _RunEditorAsAdminS, which runs Aedit.exe as admin in current interactive session.
//If fails, runs Aedit normally. Fails in non-admin session or if there is no scheduled task (program not installed?), or if task disabled.
//Aedit, if started in admin session not as admin, runs the scheduled task itself, but then slightly slower, because Aedit is a .NET app.
//Why not to run Aedit directly from the scheduled task?
//1. I don't know how to create a task that runs in current interactive session. Need multiple scheduled tasks, one for each user?
//2. Now we can add uiAccess.

//Called when command line starts with "/e".
int _RunEditorAsAdminE(LPCWSTR lpCmdLine)
{
	bool isAuHomePC = GetEnvironmentVariable(L"Au.Home<PC>", nullptr, 0) > 0;

	CoInitializeEx(NULL, COINIT_MULTITHREADED);
	ITaskService* ts = nullptr;
	ITaskFolder* tf = nullptr;
	IRegisteredTask* t = nullptr;
	IRunningTask* rt = nullptr;
	if(0 != CoCreateInstance(CLSID_TaskScheduler, nullptr, CLSCTX_INPROC_SERVER, __uuidof(ITaskService), (void**)&ts)) return 1;
	VARIANT v = {};
	if(0 != ts->Connect(v, v, v, v)) return 2;
	if(0 != ts->GetFolder(SysAllocString(L"Au"), &tf)) return 3;
	if(0 != tf->GetTask(SysAllocString(isAuHomePC ? L"_Aedit" : L"Aedit"), &t)) return 4;
	v.vt = VT_BSTR; v.bstrVal = SysAllocString(lpCmdLine);
	if(0 != t->Run(v, &rt)) return 5;
	//DWORD pid; if(0 == rt->get_EnginePID(&pid)) AllowSetForegroundWindow(pid); //usually fails. Au.CL.exe process exits quickly, we are late.
	rt->Release();
	t->Release();
	tf->Release();
	ts->Release();
	CoUninitialize();
	return 0;
}

//Called when command line starts with "/s". This process is running as SYSTEM.
int _RunEditorAsAdminS(LPCWSTR lpCmdLine)
{
	HANDLE hToken, hToken2;
	DWORD sesId = WTSGetActiveConsoleSessionId(); if(sesId == -1 || sesId == 0) return 1;
	if(!WTSQueryUserToken(sesId, &hToken)) return 2;
	DWORD dwSize;
	if(GetTokenInformation(hToken, TokenLinkedToken, &hToken2, sizeof(hToken2), &dwSize)) { //fails if non-admin user or if UAC turned off
		CloseHandle(hToken);
		hToken = hToken2;

		//rejected: add uiAccess.
		//DWORD uiAccess=1; SetTokenInformation(hToken, TokenUIAccess, &uiAccess, 4);

		//With uiAccess works better in some cases, eg on Win8 can set a window on top of metro.
		//Cannot use it because of SetParent API bug: fails if the new parent window is topmost and the old parent isn't (or is 0).
		//	SetParent is extensively used by WinForms, to move parked controls from the parking window (message-only) to the real parent window.
		//	Then, if the window is topmost, SetParent fails and the control remains hidden on the parking window.
		//	Also, if it is the first control in form, form is inactive and like disabled. Something activates the parking window instead.
		//It seems uiAccess mode is little tested by Microsoft and little used by others. I even did not find anything about this bug.
		//I suspect this mode also caused some other anomalies.
		//	Eg sometimes activating the editor window stops working normally: it becomes active but stays behind other windows.
		
	} //else MBox(L"GetTokenInformation failed");

	PROCESS_INFORMATION pi;
	STARTUPINFOW si = { sizeof(si) };
	si.dwFlags = STARTF_FORCEOFFFEEDBACK;
	si.lpDesktop = (LPWSTR)L"winsta0\\default";
	LPVOID eb = nullptr; if(!CreateEnvironmentBlock(&eb, hToken, 0)) return 3;

	auto s = (LPWSTR)malloc(wcslen(lpCmdLine) * 2 + 100);
	s[0] = 0; wcscat(s, L"Aedit.exe /n"); // /n - don't try to restart as admin
	if(*lpCmdLine && 0 != wcscmp(lpCmdLine, L"$(Arg0)")) { // $(Arg0) - the task started from Task Scheduler UI
		wcscat(s, L" ");
		wcscat(s, lpCmdLine);
	}

	if(!CreateProcessAsUserW(hToken, nullptr, s, nullptr, nullptr, false, CREATE_UNICODE_ENVIRONMENT, eb, nullptr, &si, &pi)) {
		//MBox(L"CreateProcessAsUserW: %i", GetLastError());
		return 4;
	}
	CloseHandle(pi.hThread);
	CloseHandle(pi.hProcess);
	//AllowSetForegroundWindow(pi.dwProcessId); //fails

	free(s);
	DestroyEnvironmentBlock(eb);

	CloseHandle(hToken);
	return 0;
}

//Runs Aedit.exe as admin if possible, else as standard user.
int _RunEditor(LPCWSTR lpCmdLine)
{
	int R = _RunEditorAsAdminE(lpCmdLine);
	if(0 != R) {
		PROCESS_INFORMATION pi;
		STARTUPINFOW si = { sizeof(si) };
		si.dwFlags = STARTF_FORCEOFFFEEDBACK;
		wchar_t e[] = L"Aedit.exe /n"; // /n - don't try to restart as admin
		if(!CreateProcessW(nullptr, e, nullptr, nullptr, false, 0, nullptr, nullptr, &si, &pi)) return -1;
		CloseHandle(pi.hThread);
		CloseHandle(pi.hProcess);
		AllowSetForegroundWindow(pi.dwProcessId);
	}
	return R;
}

struct TaskResults
{
	HANDLE hPipe, hOut;

	TaskResults() { hPipe = 0; hOut = 0; };

	bool Init(LPWSTR& cmdLine)
	{
		hOut = GetStdHandle(STD_OUTPUT_HANDLE);
		if(hOut == 0) { //cmd.exe? Note: in cmd execute this to change cmd console code page to UTF-8: chcp 65001
			AttachConsole(ATTACH_PARENT_PROCESS);
			hOut = GetStdHandle(STD_OUTPUT_HANDLE);
		}
		if(hOut == 0) return false;

		DWORD tid = GetCurrentThreadId();
		wchar_t pipeName[30] = LR"(\\.\pipe\Au.CL-)"; _itow(tid, pipeName + 15, 10);
		_SecurityAttributes sa(L"D:(A;;0x12019b;;;AU)"); //like of PipeSecurity that allows ReadWrite for AuthenticatedUserSid
		hPipe = CreateNamedPipeW(pipeName,
			PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED, //use async pipe because also need to wait for task process exit
			PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_REJECT_REMOTE_CLIENTS,
			1, 0, 0, 0, &sa);
		if(hPipe == INVALID_HANDLE_VALUE) { Print(L"%i", GetLastError()); return false; }

		//cmdLine = pipeName + " " + cmdLine
		int len1 = wcslen(pipeName), len2 = wcslen(cmdLine);
		LPWSTR t = (LPWSTR)malloc(2 * (len1 + len2 + 2));
		wcscpy(t, pipeName); t[len1] = ' '; wcscpy(t + len1 + 1, cmdLine);
		cmdLine = t;

		return true;
	}

	bool WaitAndRead(HANDLE hProcess)
	{
		bool R = false;
		HANDLE ev = CreateEventW(nullptr, true, false, nullptr);
		const int bLen = 5000; wchar_t b16[bLen]; char b8[bLen * 3];
		for(;;) {
			OVERLAPPED o = { }; o.hEvent = ev;
			if(!ConnectNamedPipe(hPipe, &o)) {
				int e = GetLastError();
				if(e != ERROR_PIPE_CONNECTED) {
					if(e != ERROR_IO_PENDING) { Print(L"ConnectNamedPipe: %i", e); break; }
					HANDLE ha[2] = { ev, hProcess };
					int wr = WaitForMultipleObjects(2, ha, false, INFINITE);
					if(wr != 0) { CancelIo(hPipe); R = true; break; } //task ended
					DWORD u1 = 0; if(!GetOverlappedResult(hPipe, &o, &u1, false)) { Print(L"GetOverlappedResult: %i", GetLastError()); break; }
				}
			}

			BOOL ok; DWORD n;
			while(((ok = ReadFile(hPipe, b16, sizeof(b16), &n, nullptr)) || (GetLastError() == ERROR_MORE_DATA)) && n > 0) {
				n = WideCharToMultiByte(CP_UTF8, 0, b16, n / 2, b8, sizeof(b8), nullptr, nullptr);
				WriteFile(hOut, b8, n, nullptr, nullptr);
				if(ok) break;
				//note: MSDN says must use OVERLAPPED with ReadFile too, but works without it.
			}
			if(!ok) Print(L"ReadFile: n=%i, err=%i", n, GetLastError());
			DisconnectNamedPipe(hPipe);
			if(!ok) break;
		}
		CloseHandle(ev);
		CloseHandle(hPipe);
		return R;
	}
};

enum ERunResult
{
	//the first 4 are as in script.ERunResult.
	//we return these codes -1:
	failed = 0,
	deferred = -1,
	notFound = -2,
	editorThread = -3,
	//or we return:
	noEditor = -10,
	cannotWait = -11,
	cannotGetResult = -12,
};

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPWSTR s, int nCmdShow)
{
	if(!*s)return 0;
	if(*s == '/') {
		s++; int c = *s++; if(!c || *s > ' ') return 0; if(*s) s++;
		switch(c) {
		case 'e': //run Aedit.exe as admin through Task Scheduler task that starts this program with commandline /s as SYSTEM. Run as user if fails.
			return _RunEditor(s);
		case 's': //this process is started from Task Scheduler as SYSTEM. Run Aedit.exe as admin in current interactive session.
			return _RunEditorAsAdminS(s);
		default: return 0;
		}
	}

	//Find editor's message-only window. Start editor if need.
	HWND w = 0;
	for(int i = 0; i < 1000; i++) { //if we started editor process, wait until it fully loaded, then it creates the message-only window
		w = FindWindowExW(HWND_MESSAGE, 0, L"Aedit.m3gVxcTJN02pDrHiQ00aSQ", nullptr);
		if(w) break;
		if(i == 0) { if(0 != _RunEditor(L"")) return noEditor; }
		Sleep(15);
	}
	if(!w) return noEditor;

	int mode = 0; //1 - wait, 3 - wait and get script.writeResult output
	TaskResults tr;
	if(*s == '*') {
		mode = 1;
		if(*(++s) == '*') {
			if(tr.Init(++s)) mode = 3;
			else return cannotGetResult;
		}
	}

	//this code is like in script.cs

	COPYDATASTRUCT d;
	d.dwData = 99;
	d.cbData = wcslen(s) * 2;
	d.lpData = (PVOID)s;
	int pid = (int)SendMessageW(w, WM_COPYDATA, mode, (LPARAM)&d);

	switch(pid) {
	case editorThread: if(!(mode & 2)) return 0; //valid except when need to get script.writeResult output
	case failed: case deferred: case notFound: return pid - 1;
	}

	if(mode & 1) {
		HANDLE hProcess = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
		if(hProcess == 0) return cannotWait;

		int err = 0;
		if(mode & 2) {
			if(!tr.WaitAndRead(hProcess)) err = cannotGetResult;
		} else {
			if(0 != WaitForSingleObject(hProcess, INFINITE)) err = cannotWait;
		}

		if(err == 0 && !GetExitCodeProcess(hProcess, (DWORD*)&pid)) pid = INT_MIN;
		CloseHandle(hProcess);
		if(err) return err;
	}

	return pid;
}
