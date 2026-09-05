MODULE DrawRobotStudioMcp
  ! Continuous single-line wordmark for the Project9 IRB120 virtual controller.
  ! Existing module backup is stored outside this source under artifacts/.
  TASK PERS wobjdata wobjText := [FALSE,TRUE,"",[[370,-150,200],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
  CONST jointtarget jHome := [[0,0,0,0,30,0],[9E9,9E9,9E9,9E9,9E9,9E9]];
  CONST jointtarget jPhoto := [[150,0,0,0,30,0],[9E9,9E9,9E9,9E9,9E9,9E9]];
  VAR robtarget p;
  PERS num lettersDone := 0;
  PROC main()
    ConfL \Off;
    ConfJ \Off;
    lettersDone := 0;
    MoveAbsJ jHome,v200,fine,tool0;
    SetPoint 0,0,60;
    MoveJ p,v100,fine,tool0\WObj:=wobjText;
    WaitTime 2;
    Pt 0,0;
    ! Letter 1: r
    Pt 0,0;
    Pt 0,22;
    Pt 0,15;
    ArcPt 2.05025253,19.94974747,7,22,FALSE;
    ArcPt 10.5,21.06217783,13.06217783,18.5,TRUE;
    ArcPt 10.5,21.06217783,7,22,FALSE;
    ArcPt 2.05025253,19.94974747,0,15,FALSE;
    Pt 0,0;
    Pt 14,0;
    lettersDone := 1;
    ! Letter 2: o
    Pt 20,0;
    DrawO 20;
    Pt 34,0;
    lettersDone := 2;
    ! Letter 3: b
    Pt 40,0;
    Pt 40,32;
    Pt 40,0;
    DrawO 40;
    Pt 54,0;
    lettersDone := 3;
    ! Letter 4: o
    Pt 60,0;
    DrawO 60;
    Pt 74,0;
    lettersDone := 4;
    ! Letter 5: t
    Pt 80,0;
    Pt 87,0;
    Pt 87,32;
    Pt 87,22;
    Pt 80,22;
    Pt 94,22;
    Pt 87,22;
    Flow 87,4;
    ArcPt 88.17157288,1.17157288,91,0,FALSE;
    Pt 94,0;
    lettersDone := 5;
    ! Letter 6: s
    Pt 100,0;
    Pt 101.5,0;
    Flow 107,0;
    ArcPt 110.8890873,1.6109127,112.5,5.5,FALSE;
    ArcPt 110.8890873,9.3890873,107,11,FALSE;
    ArcPt 103.1109127,12.6109127,101.5,16.5,FALSE;
    ArcPt 103.1109127,20.3890873,107,22,FALSE;
    Pt 112.5,22;
    Flow 107,22;
    ArcPt 103.1109127,20.3890873,101.5,16.5,FALSE;
    ArcPt 103.1109127,12.6109127,107,11,FALSE;
    ArcPt 110.8890873,9.3890873,112.5,5.5,FALSE;
    ArcPt 110.8890873,1.6109127,107,0,FALSE;
    Pt 101.5,0;
    Pt 114,0;
    lettersDone := 6;
    ! Letter 7: t
    Pt 120,0;
    Pt 127,0;
    Pt 127,32;
    Pt 127,22;
    Pt 120,22;
    Pt 134,22;
    Pt 127,22;
    Flow 127,4;
    ArcPt 128.17157288,1.17157288,131,0,FALSE;
    Pt 134,0;
    lettersDone := 7;
    ! Letter 8: u
    Pt 140,0;
    Pt 140,22;
    Flow 140,7;
    ArcPt 142.05025253,2.05025253,147,0,FALSE;
    ArcPt 151.94974747,2.05025253,154,7,FALSE;
    Pt 154,22;
    Pt 154,0;
    lettersDone := 8;
    ! Letter 9: d
    Pt 160,0;
    DrawO 160;
    Pt 174,0;
    Pt 174,32;
    Pt 174,0;
    lettersDone := 9;
    ! Letter 10: i
    Pt 180,0;
    Pt 187,0;
    Pt 187,22;
    Pt 187,0;
    Pt 194,0;
    lettersDone := 10;
    ! Letter 11: o
    Pt 200,0;
    DrawO 200;
    Pt 214,0;
    lettersDone := 11;
    ! Letter 12: -
    Pt 220,0;
    Pt 227,0;
    Pt 227,11;
    Pt 220,11;
    Pt 234,11;
    Pt 227,11;
    Pt 227,0;
    Pt 234,0;
    lettersDone := 12;
    ! Letter 13: m
    Pt 240,0;
    Pt 240,22;
    Pt 240,18.5;
    ArcPt 241.02512627,20.97487373,243.5,22,FALSE;
    ArcPt 245.97487373,20.97487373,247,18.5,FALSE;
    Pt 247,0;
    Flow 247,18.5;
    ArcPt 248.02512627,20.97487373,250.5,22,FALSE;
    ArcPt 252.97487373,20.97487373,254,18.5,FALSE;
    Pt 254,0;
    lettersDone := 13;
    ! Letter 14: c
    Pt 274,0;
    Pt 267,0;
    ArcPt 270.5,0.93782217,273.06217783,3.5,TRUE;
    ArcPt 270.5,0.93782217,267,0,FALSE;
    ArcPt 262.05025253,2.05025253,260,7,FALSE;
    Flow 260,15;
    ArcPt 262.05025253,19.94974747,267,22,FALSE;
    ArcPt 270.5,21.06217783,273.06217783,18.5,TRUE;
    ArcPt 270.5,21.06217783,267,22,FALSE;
    ArcPt 262.05025253,19.94974747,260,15,FALSE;
    Flow 260,7;
    ArcPt 262.05025253,2.05025253,267,0,FALSE;
    Pt 274,0;
    lettersDone := 14;
    ! Letter 15: p
    Pt 280,0;
    Pt 280,-8;
    Pt 280,22;
    Pt 280,15;
    ArcPt 282.05025253,19.94974747,287,22,FALSE;
    ArcPt 291.94974747,19.94974747,294,15,FALSE;
    ArcPt 291.94974747,10.05025253,287,8,FALSE;
    ArcPt 282.05025253,10.05025253,280,15,TRUE;
    Pt 280,0;
    Pt 294,0;
    lettersDone := 15;
    SetPoint 294,0,60;
    MoveL p,v100,fine,tool0\WObj:=wobjText;
    MoveAbsJ jHome,v100,fine,tool0;
    MoveAbsJ jPhoto,v100,fine,tool0;
  ENDPROC
  PROC DrawO(num x)
    VAR robtarget via;
    ! A 14 x 22 mm capsule: radius 7 mm, tangent vertical sides.
    ! Quarter-circle midpoint offset: 7 * (1 - sqrt(0.5)).
    Pt x+7,0;
    SetPoint x+2.05025253,2.05025253,0;
    via := p;
    SetPoint x,7,0;
    MoveC via,p,v60,z0,tool0\WObj:=wobjText;
    SetPoint x,15,0;
    MoveL p,v60,z0,tool0\WObj:=wobjText;
    SetPoint x+2.05025253,19.94974747,0;
    via := p;
    SetPoint x+7,22,0;
    MoveC via,p,v60,z0,tool0\WObj:=wobjText;
    SetPoint x+11.94974747,19.94974747,0;
    via := p;
    SetPoint x+14,15,0;
    MoveC via,p,v60,z0,tool0\WObj:=wobjText;
    SetPoint x+14,7,0;
    MoveL p,v60,z0,tool0\WObj:=wobjText;
    SetPoint x+11.94974747,2.05025253,0;
    via := p;
    SetPoint x+7,0,0;
    MoveC via,p,v60,fine,tool0\WObj:=wobjText;
  ENDPROC
  PROC Flow(num x,num y)
    SetPoint x,y,0;
    MoveL p,v60,z0,tool0\WObj:=wobjText;
  ENDPROC
  PROC ArcPt(num mx,num my,num x,num y,bool stopAtEnd)
    VAR robtarget via;
    SetPoint mx,my,0;
    via := p;
    SetPoint x,y,0;
    IF stopAtEnd THEN
      MoveC via,p,v60,fine,tool0\WObj:=wobjText;
    ELSE
      MoveC via,p,v60,z0,tool0\WObj:=wobjText;
    ENDIF
  ENDPROC
  PROC SetPoint(num x,num y,num z)
    p := [[-y,x,z],[0,0,1,0],[0,0,0,0],[9E9,9E9,9E9,9E9,9E9,9E9]];
  ENDPROC
  PROC Pt(num x,num y)
    SetPoint x,y,0;
    MoveL p,v60,fine,tool0\WObj:=wobjText;
  ENDPROC
ENDMODULE
