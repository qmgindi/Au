 \Dialog_Editor

if(!ShowDialog("" 0 0)) ret

 BEGIN DIALOG
 0 "" 0x90C80AC8 0x0 0 0 350 188 "Dialog[9]"
 3 ComboBoxEx32 0x54030002 0x0 0 12 84 14 ""
 4 msctls_hotkey32 0x54030000 0x200 180 12 76 16 ""
 5 msctls_progress32 0x54030000 0x0 92 12 86 16 ""
 6 msctls_statusbar32 0x54030100 0x0 0 174 350 14 ""
 7 msctls_trackbar32 0x54030001 0x0 260 12 58 16 ""
 8 msctls_updown32 0x54030000 0x0 332 16 11 14 ""
 10 ScrollBar 0x54030000 0x0 4 52 66 13 ""
 11 SysAnimate32 0x54830000 0x0 92 32 92 14 ""
 12 SysDateTimePick32 0x56030009 0x0 192 32 56 14 ""
 13 SysHeader32 0x54030083 0x0 200 52 56 16 "Header"
 14 SysIPAddress32 0x54030000 0x200 256 32 82 14 ""
 15 SysLink 0x54020000 0x0 100 52 64 14 "Link"
 16 SysListView32 0x54030005 0x0 20 72 82 46 ""
 17 SysMonthCal32 0x54030000 0x0 108 72 96 48 ""
 18 SysPager 0x50830001 0x0 232 72 34 34 ""
 20 SysTreeView32 0x54030000 0x0 20 124 82 46 ""
 21 ToolbarWindow32 0x54030000 0x0 0 0 350 17 ""
 22 #32770 0x54030000 0x0 108 124 96 48 ""
 23 Button 0x5403200C 0x0 272 144 68 24 "Button"
 24 Static 0x54000200 0x0 72 32 20 10 "Anim:"
 25 Static 0x54000200 0x0 208 72 48 12 "Pager:"
 26 Static 0x54000200 0x0 0 72 48 12 "LV:"
 27 Static 0x54000200 0x0 0 124 48 13 "TV:"
 28 Static 0x54000200 0x0 168 52 26 13 "Header:"
 30 ReBarWindow32 0x5403000E 0x0 216 120 408 0 ""
 19 SysTabControl32 0x54030040 0x0 272 68 72 48 ""
 END DIALOG
 DIALOG EDITOR: "" 0x2040108 "*" "" "" ""