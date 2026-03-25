MODULE McpModule
    ! v8: Key fix - use MoveJ HOME (not MoveAbsJ jHome) so solver picks safe J5
    ! After each place: MoveAbsJ jCalib (J5->0) then MoveJ HOME (solver picks low J5)
    ! No my_power_on, grPlaceBase_y=-400, dynamic approach
    PERS tooldata TCP_VentosaTool:=[TRUE,[[0,0,184],[1,0,0,0]],[1,[0,-0.818,79.529],[1,0,0,0],0,0,0]];
    TASK PERS wobjdata WO_Pick:=[FALSE,TRUE,"",[[846,535,176],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
    TASK PERS wobjdata WO_Place_gr:=[FALSE,TRUE,"",[[566,1365,-259],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
    CONST robtarget HOME:=[[1104.222103807,0,1137],[0.5,0,0.866025404,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST jointtarget jCalib:=[[0,0,0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST robtarget grPickHigh:=[[210.484,-535.003,659.5],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST robtarget grPickMid:=[[210.484,-535.003,300],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST robtarget grPickLow:=[[210.484,-535.003,180],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST robtarget grPlaceBase:=[[-500,-400,144],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST num GR_HEIGHT:=200;
    CONST num GR_SPACING:=210;
    VAR num box_count:=0;

    PROC main()
        ConfJ \Off;
        ConfL \Off;
        MoveJ HOME,v1000,fine,TCP_VentosaTool\WObj:=wobj0;
        box_count:=0;
        FOR row FROM -1 TO 1 DO
            FOR col FROM -1 TO 1 DO
                GenPickPlace row*GR_SPACING, col*GR_SPACING, 0;
            ENDFOR
        ENDFOR
        TPWrite "Layer 1 complete (9 green blocks).";
        FOR row FROM 0 TO 1 DO
            FOR col FROM 0 TO 1 DO
                GenPickPlace (row*GR_SPACING)-(GR_SPACING/2), (col*GR_SPACING)-(GR_SPACING/2), GR_HEIGHT;
            ENDFOR
        ENDFOR
        TPWrite "Layer 2 complete (4 green blocks).";
        GenPickPlace 0, 0, 2*GR_HEIGHT;
        TPWrite "3D Green Pyramid complete: 14 blocks (9-4-1).";
        MoveJ HOME,v1000,fine,TCP_VentosaTool\WObj:=wobj0;
    ENDPROC

    PROC GenPickPlace(num x_off, num y_off, num z_off)
        Set DO_Caja_gr;
        WaitTime 2;
        Reset DO_Caja_gr;
        WaitUntil DI_Sensor_Inf=1 AND DI_Sensor_Sup=1;
        WaitTime 0.5;
        PickGreen;
        PlaceGreen x_off, y_off, z_off;
        box_count:=box_count+1;
    ENDPROC

    PROC PickGreen()
        MoveJ grPickHigh,v1000,z100,TCP_VentosaTool\WObj:=WO_Pick;
        MoveL grPickMid,v1000,z100,TCP_VentosaTool\WObj:=WO_Pick;
        MoveLDO grPickLow,v500,fine,TCP_VentosaTool\WObj:=WO_Pick,DO_Ventosa,1;
        WaitTime 1;
        MoveL grPickMid,v500,z100,TCP_VentosaTool\WObj:=WO_Pick;
        MoveL grPickHigh,v1000,z100,TCP_VentosaTool\WObj:=WO_Pick;
    ENDPROC

    PROC PlaceGreen(num x_off, num y_off, num z_off)
        VAR num release_z;
        VAR num approach_z;
        release_z:=z_off+GR_HEIGHT+40;
        approach_z:=release_z+200;
        IF approach_z<400 approach_z:=400;
        MoveJ offs(grPlaceBase,x_off,y_off,approach_z),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_gr;
        MoveL offs(grPlaceBase,x_off,y_off,release_z+100),v300,z50,TCP_VentosaTool\WObj:=WO_Place_gr;
        MoveLDO offs(grPlaceBase,x_off,y_off,release_z),v100,fine,TCP_VentosaTool\WObj:=WO_Place_gr,DO_Ventosa,0;
        WaitTime 1.5;
        MoveL offs(grPlaceBase,x_off,y_off,release_z+100),v300,z100,TCP_VentosaTool\WObj:=WO_Place_gr;
        MoveL offs(grPlaceBase,x_off,y_off,approach_z),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_gr;
        MoveAbsJ jCalib,v1000,z100,TCP_VentosaTool;
        MoveJ HOME,v1000,z100,TCP_VentosaTool\WObj:=wobj0;
    ENDPROC
ENDMODULE
