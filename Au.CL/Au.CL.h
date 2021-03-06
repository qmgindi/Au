#pragma once

#include "stdafx.h"


//#if _DEBUG //this project does not have Debug config
#if false
void Print(LPCWSTR frm, ...)
{
	if(!frm) frm = L"";
	wchar_t s[1028];
	wvsprintfW(s, frm, (va_list)(&frm + 1));
	HWND w = FindWindowW(L"QM_Editor", nullptr);
	SendMessageW(w, WM_SETTEXT, -1, (LPARAM)s);
}

void MBox(LPCWSTR frm, ...)
{
	if(!frm) frm = L"";
	wchar_t s[1028];
	wvsprintfW(s, frm, (va_list)(&frm + 1));
	DWORD r;
	WTSSendMessageW(0, WTSGetActiveConsoleSessionId(), (LPWSTR)L"Au.CL.exe", 9 * 2, s, wcslen(s) * 2, MB_TOPMOST | MB_SETFOREGROUND, 0, &r, true);
	//FUTURE: add to Au
}
#else
#define Print __noop
#define MBox __noop
#endif

class _SecurityAttributes :public SECURITY_ATTRIBUTES
{
public:
	_SecurityAttributes(LPCWSTR securityDescriptor)
	{
		nLength = sizeof(SECURITY_ATTRIBUTES);
		if(!ConvertStringSecurityDescriptorToSecurityDescriptor(securityDescriptor, 1, &lpSecurityDescriptor, nullptr))
			Print(L"SECURITY_ATTRIBUTES: %i", GetLastError());
	}

	~_SecurityAttributes() {  LocalFree(lpSecurityDescriptor); }
};
