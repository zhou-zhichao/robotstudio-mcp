MODULE McpModule
    PERS tooldata TCP_VentosaTool:=[TRUE,[[0,0,184],[1,0,0,0]],[1,[0,-0.818,79.529],[1,0,0,0],0,0,0]];
    TASK PERS wobjdata WO_Pick:=[FALSE,TRUE,"",[[846,535,176],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];
    TASK PERS wobjdata WO_Place_pq:=[FALSE,TRUE,"",[[522,-650,-259],[1,0,0,0]],[[0,0,0],[1,0,0,0]]];

    CONST robtarget HOME:=[[1104.222103807,0,1137],[0.5,0,0.866025404,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];

    CONST robtarget pqPickHigh:=[[210.484,-535.003,659.5],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST robtarget pqPickMid:=[[210.484,-535.003,300],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];
    CONST robtarget pqPickLow:=[[210.484,-535.003,80],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];

    CONST robtarget pqPlaceBase:=[[-500,-300,144],[0,0,1,0],[0,0,0,0],[9E+09,9E+09,9E+09,9E+09,9E+09,9E+09]];

    CONST num PQ_HEIGHT:=100;
    CONST num PQ_SPACING:=210;

    VAR num box_count:=0;

    PROC main()
        MoveJ HOME,v1000,fine,TCP_VentosaTool\WObj:=wobj0;
        box_count:=0;

        ! === 3D Pyramid: Layer 1 (3x3=9), Layer 2 (2x2=4), Layer 3 (1x1=1) = 14 blocks ===

        ! Layer 1: 3x3 grid at z=0
        FOR row FROM -1 TO 1 DO
            FOR col FROM -1 TO 1 DO
                GenPickPlace row*PQ_SPACING, col*PQ_SPACING, 0;
            ENDFOR
        ENDFOR

        TPWrite "Layer 1 complete (9 blocks).";

        ! Layer 2: 2x2 grid at z=PQ_HEIGHT, offset by half spacing
        FOR row FROM 0 TO 1 DO
            FOR col FROM 0 TO 1 DO
                GenPickPlace (row*PQ_SPACING)-(PQ_SPACING/2), (col*PQ_SPACING)-(PQ_SPACING/2), PQ_HEIGHT;
            ENDFOR
        ENDFOR

        TPWrite "Layer 2 complete (4 blocks).";

        ! Layer 3: 1 block at center, z=2*PQ_HEIGHT
        GenPickPlace 0, 0, 2*PQ_HEIGHT;

        TPWrite "3D Pyramid complete: 14 orange blocks (9-4-1).";
        MoveJ HOME,v1000,fine,TCP_VentosaTool\WObj:=wobj0;
    ENDPROC

    PROC GenPickPlace(num x_off, num y_off, num z_off)
        Set DO_Caja_pq;
        WaitTime 2;
        Reset DO_Caja_pq;

        WaitUntil DI_Sensor_Inf=1 AND DI_Sensor_Sup=0;
        WaitTime 0.5;

        PickOrange;
        PlaceOrange x_off, y_off, z_off;

        box_count:=box_count+1;
    ENDPROC

    PROC PickOrange()
        MoveJ pqPickHigh,v1000,z100,TCP_VentosaTool\WObj:=WO_Pick;
        MoveL pqPickMid,v1000,z100,TCP_VentosaTool\WObj:=WO_Pick;
        MoveLDO pqPickLow,v500,fine,TCP_VentosaTool\WObj:=WO_Pick,DO_Ventosa,1;
        WaitTime 1;
        MoveL pqPickMid,v500,z100,TCP_VentosaTool\WObj:=WO_Pick;
        MoveL pqPickHigh,v1000,z100,TCP_VentosaTool\WObj:=WO_Pick;
    ENDPROC

    PROC PlaceOrange(num x_off, num y_off, num z_off)
        VAR num release_z;
        release_z:=z_off+PQ_HEIGHT+40;

        MoveJ offs(pqPlaceBase,x_off,y_off,600),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
        MoveL offs(pqPlaceBase,x_off,y_off,release_z+100),v300,z50,TCP_VentosaTool\WObj:=WO_Place_pq;
        MoveLDO offs(pqPlaceBase,x_off,y_off,release_z),v100,fine,TCP_VentosaTool\WObj:=WO_Place_pq,DO_Ventosa,0;
        WaitTime 1.5;
        MoveL offs(pqPlaceBase,x_off,y_off,release_z+100),v300,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
        MoveL offs(pqPlaceBase,x_off,y_off,600),v1000,z100,TCP_VentosaTool\WObj:=WO_Place_pq;
        MoveJ HOME,v1000,z100,TCP_VentosaTool\WObj:=wobj0;
    ENDPROC
ENDMODULE
