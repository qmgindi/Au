int w=win("Library" "Mozilla*WindowClass" "" 0x804)
act w
Acc a1.Find(w "OUTLINE" "" "class=MozillaWindowClass" 0x1084)
Acc a2.Find(w "TEXT" "Location:" "class=MozillaWindowClass[]state=0x0 0x20000040" 0x1085)
a1.Select(3)
 spe 50
rep ;;100
	_s=a2.Value
	if _s.replacerx("(\?v=.+?)&.+" "$1" 4)>0
		 out _s
		 break
		a2.Select(3)
		key Ca
		_s.setsel
		a1.Select(3)
	key D