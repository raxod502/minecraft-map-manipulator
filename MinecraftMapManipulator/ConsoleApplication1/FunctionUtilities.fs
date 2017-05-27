﻿module FunctionUtilities

// binaryLookup("findPhi", "look", "rym", "ry", 7, 3, -180, fs) 
// looks at @e[tag=look] and binary searches 2^7 in steps of 3 at offset -180, so e.g. [-180..-178] and [-177..-175] are bottom buckets
// and fs is a list of functions applied to the values (like -179) resulting in 
// (name,val) the name of a variable objective to write and the value to write on ENTITY in scoreboard
let binaryLookup(prefix, entityTag, minSel, maxSel, exp, k, offset, fs) =
    let mutable n = 1
    for _i = 1 to exp do
        n <- n * 2
    let functions = ResizeArray()
    let outputObjectives = new System.Collections.Generic.HashSet<_>()
    let makeName(lo,hi) = sprintf "%s/do%dto%d" prefix lo hi
    let rec go(lo,hi) =
        let name = makeName(lo,hi)
        if hi-lo < k then
            functions.Add(name,[|
                for i = lo to hi do
                    for f in fs do
                        let obj,num = f i
                        outputObjectives.Add(obj) |> ignore
                        yield sprintf "execute @e[tag=%s,%s=%d,%s=%d] ~ ~ ~ scoreboard players set %s %s %d" entityTag minSel i maxSel i FunctionCompiler.ENTITY_UUID obj num
                |])
        else
            let mid = (hi-lo)/2 + lo
            let midn = mid+1
            functions.Add(name,[|
                yield sprintf "execute @e[tag=%s,%s=%d,%s=%d] ~ ~ ~ function %s:%s" entityTag minSel lo maxSel mid FunctionCompiler.FUNCTION_NAMESPACE (makeName(lo,mid))
                yield sprintf "execute @e[tag=%s,%s=%d,%s=%d] ~ ~ ~ function %s:%s" entityTag minSel midn maxSel hi FunctionCompiler.FUNCTION_NAMESPACE (makeName(midn,hi))
                |])
            go(lo,mid)
            go(midn,hi)
    go(offset,offset+n*k)
    functions.Add(prefix,[|
        sprintf "# %s" prefix
        sprintf "# inputs: an entity has already been tagged '%s'" entityTag
        sprintf "# outputs: %A" (outputObjectives |> Seq.toList)
        sprintf "function %s:%s" FunctionCompiler.FUNCTION_NAMESPACE (makeName(offset,offset+n*k))
        |])
    functions

// Note: from an F# point-of-view, it'd be better to make these vars properties of a findPhi object, but making at global F# scope reflects fact that scoreboard objectives are global
let phiScope = new FunctionCompiler.Scope()
let vphi = phiScope.RegisterVar("phi")
let vKsinPhi = phiScope.RegisterVar("KsinPhi")
let vKcosPhi = phiScope.RegisterVar("KcosPhi")
let findPhi = 
    let funcs = binaryLookup("findPhi", "look", "rym", "ry", 7, 3, -180, 
                                [fun phi -> vphi.Name,phi
                                 fun phi -> vKsinPhi.Name,int(1000.0*sin(System.Math.PI * float phi / 180.0))
                                 fun phi -> vKcosPhi.Name,int(1000.0*cos(System.Math.PI * float phi / 180.0))
                                ]) 
    let oneTimeInit = [|
        // declare variables (objectives), initialize constants, place any long-lasting objects in the world
        for v in phiScope.All() do
            yield sprintf "scoreboard objectives add %s dummy" v.Name
        |]
    FunctionCompiler.DropInModule("findPhi",oneTimeInit,funcs.ToArray())

let thetaScope = new FunctionCompiler.Scope()
let vtheta = thetaScope.RegisterVar("theta")
let vKsinTheta = thetaScope.RegisterVar("Ksintheta")
let vKcosTheta = thetaScope.RegisterVar("Kcostheta")
let findTheta = 
    let funcs = binaryLookup("findTheta", "look", "rxm", "rx", 6, 3, -90, 
                                [fun theta -> vtheta.Name,theta
                                 fun theta -> vKsinTheta.Name,int(1000.0*sin(System.Math.PI * float theta / 180.0))
                                 fun theta -> vKcosTheta.Name,int(1000.0*cos(System.Math.PI * float theta / 180.0))
                                ]) 
    let oneTimeInit = [|
        // declare variables (objectives), initialize constants, place any long-lasting objects in the world
        for v in thetaScope.All() do
            yield sprintf "scoreboard objectives add %s dummy" v.Name
        |]
    FunctionCompiler.DropInModule("findTheta",oneTimeInit,funcs.ToArray())

//////////////////////////////////////

let profileThis(suffix,pre,cmds,post) =
    let profilerFunc = FunctionCompiler.makeFunction("prof-"+suffix,[
        yield "gamerule maxCommandChainLength 999999"
        yield "gamerule commandBlockOutput false"
        yield "gamerule sendCommandFeedback false"
        yield "gamerule logAdminCommands false"

        yield "scoreboard objectives add A dummy"
        yield "scoreboard objectives add WB dummy"

        yield "scoreboard objectives setdisplay sidebar A"

        yield "execute @p ~ ~ ~ summon armor_stand ~ ~ ~ {CustomName:Timer,NoGravity:1,Invulnerable:1}" 
        yield "scoreboard players set @e[name=Timer] WB 1" 
        yield "stats entity @e[name=Timer] set QueryResult @e[name=Timer] WB" 

        yield "worldborder set 10000000" 
        yield "worldborder add 1000000 1000" 
        
        yield! pre
        for _i = 1 to 100 do
            yield sprintf "function %s:code-%s" FunctionCompiler.FUNCTION_NAMESPACE suffix
        yield! post

        yield "tellraw @a [\"done!\"]" 
        yield "execute @e[name=Timer] ~ ~ ~ worldborder get" 
        yield "scoreboard players set Time A -10000000" 
        yield "scoreboard players operation Time A += @e[name=Timer] WB" 
        yield """tellraw @a ["took ",{"score":{"name":"Time","objective":"A"}}," milliseconds"]"""
        yield "kill @e[name=Timer]"
        ])
    let dummyFunc = FunctionCompiler.makeFunction("code-"+suffix,[|
        for _i = 1 to 1000 do 
            yield! cmds 
        |])
    [| profilerFunc; dummyFunc |]

//////////////////////////////////////

open FunctionCompiler

let init      = BBN"init"
let whiletest = BBN"whiletest"
let loopbody  = BBN"loopbody"
let coda      = BBN"coda"

let raycastVars = new Scope()
// constants
let R = raycastVars.RegisterVar("R")
let ONE_THOUSAND = raycastVars.RegisterVar("ONE_THOUSAND")
// variables
let DX = raycastVars.RegisterVar("DX")
let DY = raycastVars.RegisterVar("DY")
let DZ = raycastVars.RegisterVar("DZ")
let FLIPX = raycastVars.RegisterVar("FLIPX")
let FLIPY = raycastVars.RegisterVar("FLIPY")
let FLIPZ = raycastVars.RegisterVar("FLIPZ")
let TEMP = raycastVars.RegisterVar("TEMP")
let TDX = raycastVars.RegisterVar("TDX")
let TDY = raycastVars.RegisterVar("TDY")
let TDZ = raycastVars.RegisterVar("TDZ")
let MAJOR = raycastVars.RegisterVar("MAJOR")
let TMAJOR = raycastVars.RegisterVar("TMAJOR")
let AX = raycastVars.RegisterVar("AX")
let AY = raycastVars.RegisterVar("AY")
let AZ = raycastVars.RegisterVar("AZ")

let yOffset = 5   // attempt to put all the armor stands not-in-my-face so that I can throw snowballs

// TODO only activate when holding snowball
// uses https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm
let raycastProgram = 
    Program([|findTheta;findPhi|],[|
        yield AtomicCommand("kill @e[type=armor_stand,name=RAY]")
        // dependencies
        for DropInModule(_,oneTimeInit,_) in [findPhi; findTheta] do
            yield! oneTimeInit |> Seq.map (fun cmd -> AtomicCommand(cmd))
        // SB init
        for v in raycastVars.All() do
            yield AtomicCommand(sprintf "scoreboard objectives add %s dummy" v.Name)
        // constants
        yield SB(R .= 128)
        yield SB(ONE_THOUSAND .= 1000)
        // prep code
        // TODO would be more effiicent to UUID RAY
        yield AtomicCommand "summon armor_stand 0 4 0 {CustomName:RAY,NoGravity:1,Invisible:1,Glowing:1,Invulnerable:1}"
        yield AtomicCommand "scoreboard players tag @p add look"
        |],init,dict[
        init,BasicBlock([|
            AtomicCommand(sprintf "function %s:findTheta" FunctionCompiler.FUNCTION_NAMESPACE)
            AtomicCommand(sprintf "function %s:findPhi" FunctionCompiler.FUNCTION_NAMESPACE)
            //let DX = - R cos(theta) sin(phi)
            SB(DX .= 0)
            SB(DX .-= R)
            SB(DX .*= vKcosTheta)
            SB(DX .*= vKsinPhi)
            SB(DX ./= ONE_THOUSAND)
            SB(DX ./= ONE_THOUSAND)
            //let DY = -R sin(theta)
            SB(DY .= 0)
            SB(DY .-= R)
            SB(DY .*= vKsinTheta)
            SB(DY ./= ONE_THOUSAND)
            //let DZ = R cos(theta) cos(phi)
            SB(DZ .= R)
            SB(DZ .*= vKcosTheta)
            SB(DZ .*= vKcosPhi)
            SB(DZ ./= ONE_THOUSAND)
            SB(DZ ./= ONE_THOUSAND)
(*
            // debug
            AtomicCommand(sprintf "scoreboard players operation @p %s = %s %s" DX.Name ENTITY_UUID DX.Name)
            AtomicCommand(sprintf "scoreboard players operation @p %s = %s %s" DY.Name ENTITY_UUID DY.Name)
            AtomicCommand(sprintf "scoreboard players operation @p %s = %s %s" DZ.Name ENTITY_UUID DZ.Name)
            AtomicCommand(sprintf """tellraw @a ["DX=",{"score":{"name":"@p","objective":"%s"}}," DY=",{"score":{"name":"@p","objective":"%s"}}," DZ=",{"score":{"name":"@p","objective":"%s"}}]""" DX.Name DY.Name DZ.Name)
*)
            // all D_ vars need to be positive, flip if needed and track what we flipped
            SB(FLIPX .= 0)
            SB(FLIPY .= 0)
            SB(FLIPZ .= 0)
            //if DX < 0 then
            //    DX = -DX
            //    FLIPX = true
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DX.Name (SB(TEMP .= 0).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DX.Name (SB(TEMP .-= DX).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DX.Name (SB(FLIPX .= 1).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DX.Name (SB(DX .= TEMP).AsCommand()))
            //if DY < 0 then
            //    DY = -DY
            //    FLIPY = true
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DY.Name (SB(TEMP .= 0).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DY.Name (SB(TEMP .-= DY).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DY.Name (SB(FLIPY .= 1).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DY.Name (SB(DY .= TEMP).AsCommand()))
            //if DZ < 0 then
            //    DZ = -DZ
            //    FLIPZ = true
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DZ.Name (SB(TEMP .= 0).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DZ.Name (SB(TEMP .-= DZ).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DZ.Name (SB(FLIPZ .= 1).AsCommand()))
            AtomicCommand(sprintf "execute @s[score_%s=-1] ~ ~ ~ %s" DZ.Name (SB(DZ .= TEMP).AsCommand()))
            //let TDX = DX + DX
            SB(TDX .= DX)
            SB(TDX .+= DX)
            //let TDY = DY + DY
            SB(TDY .= DY)
            SB(TDY .+= DY)
            //let TDZ = DZ + DZ
            SB(TDZ .= DZ)
            SB(TDZ .+= DZ)
            // major is the largest of the 3
            //let MAJOR = DX
            SB(MAJOR .= DX)
            //if DY - MAJOR > 0 then
            //    MAJOR = DY
            SB(TEMP .= DY)
            SB(TEMP .-= MAJOR)
            AtomicCommand(sprintf "execute @s[score_%s_min=0] ~ ~ ~ %s" TEMP.Name (SB(MAJOR .= DY).AsCommand()))
            //if DZ - MAJOR > 0 then
            //    MAJOR = DZ
            SB(TEMP .= DZ)
            SB(TEMP .-= MAJOR)
            AtomicCommand(sprintf "execute @s[score_%s_min=0] ~ ~ ~ %s" TEMP.Name (SB(MAJOR .= DZ).AsCommand()))
            //let TMAJOR = MAJOR + MAJOR
            SB(TMAJOR .= MAJOR)
            SB(TMAJOR .+= MAJOR)
            //let AX = TMAJOR - DX
            SB(AX .= TMAJOR)
            SB(AX .-= DX)
            //let AY = TMAJOR - DY
            SB(AY .= TMAJOR)
            SB(AY .-= DY)
            //let AZ = TMAJOR - DZ
            SB(AZ .= TMAJOR)
            SB(AZ .-= DZ)
            // put armor stand at right starting point
            AtomicCommand("tp @e[type=armor_stand,name=RAY] @p") // now RAY has my facing
            AtomicCommand(sprintf "tp @e[type=armor_stand,name=RAY] ~ ~%d ~" (1+yOffset)) // eyeball level (+offset)
(* TODO snap-to-grid
            AtomicCommand("execute @p ~ ~ ~ summon shulker ~ ~1 ~ {NoAI:1}") // snap to grid
            AtomicCommand("execute @e[type=shulker] ~ ~ ~ teleport @e[name=RAY] ~ ~ ~")
            AtomicCommand("tp @e[type=shulker] ~ ~-300 ~") // kill shulker
*)
            |],DirectTailCall(whiletest),MustNotYield)
        whiletest,BasicBlock([|
            |],ConditionalTailCall(Conditional[| MAJOR .>= 1 |],loopbody,coda),MustNotYield)
        loopbody,BasicBlock([|
            // remember where we are, so can back up
            AtomicCommand "execute @e[type=armor_stand,name=RAY] ~ ~ ~ summon armor_stand ~ ~ ~  {CustomName:tempAS,NoGravity:1,Invisible:1,Invulnerable:1}"
            //if AX > 0 then
            //    if FLIPX then
            //        tp RAY ~-1 ~ ~
            //    else
            //        tp RAY ~1 ~ ~
            //    AX = AX - TMAJOR
            // AX = AX + 2DX
            AtomicCommand(sprintf "execute @s[score_%s_min=1,score_%s_min=1] ~ ~ ~ tp @e[type=armor_stand,name=RAY] ~-1 ~ ~" AX.Name FLIPX.Name)
            AtomicCommand(sprintf "execute @s[score_%s_min=1,score_%s=0] ~ ~ ~ tp @e[type=armor_stand,name=RAY] ~1 ~ ~" AX.Name FLIPX.Name)
            AtomicCommand(sprintf "execute @s[score_%s_min=1] ~ ~ ~ %s" AX.Name (SB(AX .-= TMAJOR).AsCommand()))
            SB(AX .+= TDX)
            // ditto for y
            AtomicCommand(sprintf "execute @s[score_%s_min=1,score_%s_min=1] ~ ~ ~ tp @e[type=armor_stand,name=RAY] ~ ~-1 ~" AY.Name FLIPY.Name)
            AtomicCommand(sprintf "execute @s[score_%s_min=1,score_%s=0] ~ ~ ~ tp @e[type=armor_stand,name=RAY] ~ ~1 ~" AY.Name FLIPY.Name)
            AtomicCommand(sprintf "execute @s[score_%s_min=1] ~ ~ ~ %s" AY.Name (SB(AY .-= TMAJOR).AsCommand()))
            SB(AY .+= TDY)
            // ditto for z
            AtomicCommand(sprintf "execute @s[score_%s_min=1,score_%s_min=1] ~ ~ ~ tp @e[type=armor_stand,name=RAY] ~ ~ ~-1" AZ.Name FLIPZ.Name)
            AtomicCommand(sprintf "execute @s[score_%s_min=1,score_%s=0] ~ ~ ~ tp @e[type=armor_stand,name=RAY] ~ ~ ~1" AZ.Name FLIPZ.Name)
            AtomicCommand(sprintf "execute @s[score_%s_min=1] ~ ~ ~ %s" AZ.Name (SB(AZ .-= TMAJOR).AsCommand()))
            SB(AZ .+= TDZ)
            // MAJOR = MAJOR - 1
            SB(MAJOR .-= 1)
            // detect non-air and exit loop early
            SB(TEMP .= 1)
            // TODO line below not work, because uses @s for TEMP, which is wrong under /execute... need a way to abstract this idiom
            //AtomicCommand(sprintf "execute @e[name=RAY] ~ ~ ~ detect ~ ~ ~ air 0 %s" (SB(TEMP .= 0).AsCommand()))
            AtomicCommand(sprintf "execute @e[type=armor_stand,name=RAY] ~ ~ ~ detect ~ ~%d ~ air 0 execute @e[type=armor_stand,name=RAY] ~ ~ ~ detect ~ ~%d ~ air 0 scoreboard players set %s %s 0" (0-yOffset) (-1-yOffset) ENTITY_UUID TEMP.Name)
            // line above has two E-Ds to check current block and block below, since player is 2-tall and we are at eyeball level
            SB(ScoreboardPlayersConditionalSet(Conditional[|TEMP .>= 1|],MAJOR,0))
            AtomicCommand(sprintf "execute @s[score_%s_min=1] ~ ~ ~ execute @e[type=armor_stand,name=tempAS] ~ ~ ~ teleport @e[type=armor_stand,name=RAY] ~ ~ ~" TEMP.Name) // tp RAY to tempAS but preserve RAY's facing direction
            // kill tempAS
            AtomicCommand("kill @e[type=armor_stand,name=tempAS]")
            |],DirectTailCall(whiletest),MustNotYield)
        coda,BasicBlock([|
            AtomicCommand(sprintf "tp @e[type=armor_stand,name=RAY] ~ ~%d ~" -yOffset)
            AtomicCommand("execute @e[type=snowball] ~ ~ ~ tp @p @e[type=armor_stand,name=RAY]")
            AtomicCommand("kill @e[type=snowball]")
            |],DirectTailCall(init),MustWaitNTicks 1)
        ])

//////////////////////////////////////

let prngScope = new Scope()
let prng_A = prngScope.RegisterVar("prng_A")
let prng_C = prngScope.RegisterVar("prng_C")
let prng_Two = prngScope.RegisterVar("prng_Two")
let prng_Two16 = prngScope.RegisterVar("prng_Two16")
let prng_Z = prngScope.RegisterVar("prng_Z")
let prng_Mod = prngScope.RegisterVar("prng_Mod")  // the input
let prng_K = prngScope.RegisterVar("prng_K")      // the output
let prng =
    let oneTimeInit = [|
        // declare variables
        for v in prngScope.All() do
            yield sprintf "scoreboard objectives add %s dummy" v.Name
        // initialize constants
        yield sprintf "scoreboard players set %s 1103515245" (prng_A.AsCommandFragmentWithoutEntityBoundToAtS())
        yield sprintf "scoreboard players set %s 12345" (prng_C.AsCommandFragmentWithoutEntityBoundToAtS()) 
        yield sprintf "scoreboard players set %s 2" (prng_Two.AsCommandFragmentWithoutEntityBoundToAtS()) 
        yield sprintf "scoreboard players set %s 65536" (prng_Two16.AsCommandFragmentWithoutEntityBoundToAtS()) 
        // one-time-initialize variables
        yield sprintf "scoreboard players set %s 0" (prng_Z.AsCommandFragmentWithoutEntityBoundToAtS()) 
        // place any long-lasting objects in the world
    |]
    let Z = prng_Z
    let A = prng_A
    let C = prng_C
    let K = prng_K
    let Two = prng_Two
    let Two16 = prng_Two16 
    let Mod = prng_Mod
    let cmds = [|
        // compute next Z value with PRNG
        SB(Z .*= A)
        SB(Z .+= C)
        SB(Z .*= Two)  // mod 2^31
        SB(Z ./= Two)
        SB(K .= Z)
        SB(K .*= Two)
        SB(K ./= Two)
        SB(K ./= Two16) // upper 16 bits most random
        // get a number in the desired range
        SB(K .%= Mod)
        SB(K .+= Mod)  // ensure non-negative
        SB(K .%= Mod)
    |]
    let prngBody = "prngBody",cmds|>Array.map(fun c -> c.AsCommand())
    let prngMain = "prng",[|
        "# prng"
        "# inputs: prng_Mod    e.g. if it's 20, the output will be random number in range 0-19"
        "# outputs: prng_K"
        // TODO do I need this indirection?  I wanted to test this manually by calling "function lorgon111:prng", but perhaps 
        //    I should test via "execute 1-1-1-0-1 ~ ~ ~ function lorgon111:prng", and all functions assume entity is the sender?
        //    Is a trade-off; place I do intend to call it next does not have 1-1-1-0-1 as the sender, so not a slam dunk... maybe always have 2 versions of utils?
        sprintf "execute %s ~ ~ ~ function %s:prngBody" ENTITY_UUID FUNCTION_NAMESPACE
        |]
    DropInModule("prng",oneTimeInit,[|prngBody;prngMain|])

//////////////////////////////////////

// conway life

// one level cmd blocks
let conwayLife = 
    let oneTimeInit = [|
        "scoreboard objectives add A dummy"  // 2-state iteration
        "scoreboard objectives add N dummy"  // neightbor count
        "scoreboard objectives add R dummy"  // is it running?
        "gamerule maxCommandChainLength 999999"
        "summon armor_stand ~ 0 ~ {Invisible:1b,NoGravity:1b}"
        "scoreboard players set @p A 1"
        "scoreboard players set @e[type=armor_stand] A 1"
        |]

    let count_neighbors = "count_neighbors",[|
        "scoreboard players set @s N 0"
        "execute @s ~ ~ ~ detect ~-1 4 ~-1 wool 15 scoreboard players add @s N 1"
        "execute @s ~ ~ ~ detect ~-1 4 ~-0 wool 15 scoreboard players add @s N 1"
        "execute @s ~ ~ ~ detect ~-1 4 ~+1 wool 15 scoreboard players add @s N 1"
        "execute @s ~ ~ ~ detect ~-0 4 ~-1 wool 15 scoreboard players add @s N 1"
        "execute @s ~ ~ ~ detect ~-0 4 ~+1 wool 15 scoreboard players add @s N 1"
        "execute @s ~ ~ ~ detect ~+1 4 ~-1 wool 15 scoreboard players add @s N 1"
        "execute @s ~ ~ ~ detect ~+1 4 ~-0 wool 15 scoreboard players add @s N 1"
        "execute @s ~ ~ ~ detect ~+1 4 ~+1 wool 15 scoreboard players add @s N 1"
        |]
    let has_buffer_neighbor = "has_buffer_neighbor",[|
        "scoreboard players set @s N 0"
        "execute @s ~ ~ ~ detect ~-1 3 ~-1 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~-1 3 ~-0 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~-1 3 ~+1 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~-0 3 ~-1 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~-0 3 ~-0 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~-0 3 ~+1 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~+1 3 ~-1 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~+1 3 ~-0 wool 15 scoreboard players add @s N 1"
        "execute @s[score_N=0] ~ ~ ~ detect ~+1 3 ~+1 wool 15 scoreboard players add @s N 1"
        |]
    // note that the 2-cycles is
    // @p A = 0 -> run all the check1, compute buffer, do no scheduling
    // @p A = 1 -> double-buffer the check1 results, schedule blocks around all (buffer) blacks, delete all check1 command blocks that are (buffer) white and have no (buffer) black neighbors
    let check1 = "check1",[|
        "teleport @e[type=armor_stand,score_R_min=1] ~ ~ ~"
        "execute @e[type=armor_stand,score_A=0,score_A_min=0,score_R_min=1] ~ ~ ~ function conway:check1body"
        "execute @e[type=armor_stand,score_A=1,score_A_min=1,score_R_min=1] ~ ~ ~ function conway:check1part2"
        |]
    let check1body = "check1body",[|
        "function conway:count_neighbors"
        "clone ~ 4 ~ ~ 4 ~ ~ 3 ~"  // assume nothing changes to start
        "execute @s[score_N=1] ~ ~ ~ detect ~ 4 ~ wool 15 setblock ~ 3 ~ wool 0"
        "execute @s[score_N_min=4] ~ ~ ~ detect ~ 4 ~ wool 15 setblock ~ 3 ~ wool 0"
        "execute @s[score_N_min=3,score_N=3] ~ ~ ~ detect ~ 4 ~ wool 0 setblock ~ 3 ~ wool 15"
        |]
    let check1part2 = "check1part2",[|
        // double-buffer this cell
        "clone ~ 3 ~ ~ 3 ~ ~ 4 ~"
        // schedule blocks for next tick if (buffer) me is alive
        """execute @s ~ ~ ~ detect ~ 3 ~ wool 15 fill ~-1 1 ~-1 ~1 1 ~1 repeating_command_block 0 replace {auto:1b,Command:"function conway:check1"}"""
        // delete command block if (buffer) me is dead, and so are all my neighbors
        "function conway:has_buffer_neighbor"
        "execute @s[score_N=0] ~ ~ ~ setblock ~ 1 ~ air"
        |]
    let life = "life",[|
        "execute @e[type=armor_stand,score_A_min=1] ~ ~ ~ scoreboard players set @p[type=armor_stand] R 0"
        """execute @e[type=armor_stand,score_A_min=1] ~ ~ ~ scoreboard players add @p R 1 {SelectedItem:{id:"minecraft:redstone_block"}}"""
        "scoreboard players operation @e[type=armor_stand] R = @p R"
        "scoreboard players add @e[type=armor_stand,score_R_min=1] A 1"
        "scoreboard players set @e[type=armor_stand,score_R_min=1,score_A_min=2] A 0"
        """execute @e[type=bat] ~ ~ ~ fill ~-1 1 ~-1 ~1 1 ~1 repeating_command_block 0 replace {auto:1b,Command:"function conway:check1"}"""
        // todo don't run so much in the 'always' loop, when this is really only 'setup' stuff - modal?
        "execute @e[type=bat] ~ ~ ~ setblock ~ 4 ~ wool 15"
        "tp @e[type=bat] ~ ~-200 ~"
        "execute @e[type=skeleton] ~ ~ ~ setblock ~ 4 ~ wool 0"
        "tp @e[type=skeleton] ~ ~-200 ~"
        // todo add methusalah/glider eggs?
        |]
    DropInModule("life",oneTimeInit,[|
        count_neighbors
        has_buffer_neighbor
        check1
        check1body
        check1part2
        life
        |])

//////////////////////////////////////

let potionOfForesight =
    let least,most = Utilities.toLeastMost(new System.Guid(ENTITY_UUID_AS_FULL_GUID))
    let oneTimeInit = [|
        // prng too
        "scoreboard objectives add nPotionActive dummy"  // n=next
        "scoreboard objectives add potionActive dummy"
        "scoreboard objectives add riderHasHost dummy"
        "scoreboard objectives add nextNum dummy"
        sprintf "summon armor_stand -3 4 -3 {CustomName:%s,NoGravity:1,UUIDMost:%dl,UUIDLeast:%dl,Invulnerable:1}" ENTITY_UUID most least  // TODO lots of UUID fragility
        // TODO call prng_init, so only need one init call
        // TODO abstract notion of dependency to deal with this
        // TODO abstract notion of init also then installing gameLoopFunction
        |]
    let foresight_loop = "foresight_loop",[|
        "execute @p[score_nPotionActive_min=1,score_potionActive=0] ~ ~ ~ function lorgon111:turn_on"
        "execute @p[score_nPotionActive=0,score_potionActive_min=1] ~ ~ ~ function lorgon111:turn_off"
        "scoreboard players operation @p potionActive = @p nPotionActive"
        "scoreboard players remove @p nPotionActive 1"   // was init'd to numTicks, counts down here
        
        "function lorgon111:process_zombies"
        "function lorgon111:process_spiders"
        // same for spiders, etc
        
        "execute @e[tag=rider] ~ ~ ~ function lorgon111:run_rider"
        |]
    let process_zombies = "process_zombies",[|
        sprintf "scoreboard players set %s prng_Mod 100" FunctionCompiler.ENTITY_UUID 
        "execute @e[type=zombie,tag=!processed] ~ ~ ~ function lorgon111:process_zombie"
        |]
    let process_zombie = "process_zombie",[|
        "scoreboard players tag @s add processed"
        "function lorgon111:prng" // TODO namepsace
        sprintf "scoreboard players operation @s prng_K = %s prng_K" FunctionCompiler.ENTITY_UUID 
        
        // 95/100 zombie -> 0
        "scoreboard players tag @s[score_prng_K=94] add noRareLoot"
        // 4/100 zombie -> apple
        "scoreboard players tag @s[score_prng_K_min=95] add hasRareLoot"
        "scoreboard players tag @s[score_prng_K_min=95,score_prng_K=98] add hasApple"
        """entitydata @s[score_prng_K_min=95,score_prng_K=98] {DeathLootTable:"lorgon111:zombie_with_apple"}"""
        // 1/100 zombie -> diamond
        "scoreboard players tag @s[score_prng_K_min=99] add hasDiamond"
        """entitydata @s[score_prng_K_min=99] {DeathLootTable:"lorgon111:zombie_with_diamond"}"""
        
        "scoreboard players operation @s potionActive = @p potionActive"
        "execute @s[score_potionActive_min=1] ~ ~ ~ function lorgon111:turn_on_mob"
        |]
    let process_spiders = "process_spiders",[|
        sprintf "scoreboard players set %s prng_Mod 100" FunctionCompiler.ENTITY_UUID 
        "execute @e[type=spider,tag=!processed] ~ ~ ~ function lorgon111:process_spider"
        |]
    let process_spider = "process_spider",[|
        "scoreboard players tag @s add processed"
        "function lorgon111:prng" // TODO namepsace
        sprintf "scoreboard players operation @s prng_K = %s prng_K" FunctionCompiler.ENTITY_UUID 
        
        // 95/100 spider -> 0
        "scoreboard players tag @s[score_prng_K=94] add noRareLoot"
        // 4/100 spider -> flint
        "scoreboard players tag @s[score_prng_K_min=95] add hasRareLoot"
        "scoreboard players tag @s[score_prng_K_min=95,score_prng_K=98] add hasFlint"
        """entitydata @s[score_prng_K_min=95,score_prng_K=98] {DeathLootTable:"lorgon111:spider_with_flint"}"""
        // 1/100 spider -> cake
        "scoreboard players tag @s[score_prng_K_min=99] add hasCake"
        """entitydata @s[score_prng_K_min=99] {DeathLootTable:"lorgon111:spider_with_cake"}"""
        
        "scoreboard players operation @s potionActive = @p potionActive"
        "execute @s[score_potionActive_min=1] ~ ~ ~ function lorgon111:turn_on_mob"
        |]
    let run_rider = "run_rider",[|
        // @s is a rider AS, we need to tp it to its entity, or kill if its host is gone
        sprintf "scoreboard players set %s riderHasHost 0" FunctionCompiler.ENTITY_UUID
        "scoreboard players tag @s add curRider"

        // using r=3, since mob may have moved last tick (assumes mob not move more than 60 blocks per second)
        "scoreboard players operation @e[r=3,tag=!rider] nextNum -= @s nextNum"
        "execute @e[r=3,tag=!rider,score_nextNum_min=0,score_nextNum=0] ~ ~ ~ teleport @e[r=3,tag=curRider] ~ ~ ~"
        sprintf "execute @e[r=3,tag=!rider,score_nextNum_min=0,score_nextNum=0] ~ ~ ~ scoreboard players set %s riderHasHost 1" FunctionCompiler.ENTITY_UUID 
        "scoreboard players operation @e[r=3,tag=!rider] nextNum += @s nextNum"

        "scoreboard players tag @s remove curRider"
        sprintf "scoreboard players operation @s riderHasHost = %s riderHasHost" FunctionCompiler.ENTITY_UUID 
        "kill @s[score_riderHasHost=0]"
        |]
    let turn_on = "turn_on",[|
        """tellraw @a["foresight turned on"]"""
        "execute @e[tag=processed] ~ ~ ~ function lorgon111:turn_on_mob"
        |]
    let turn_on_mob = "turn_on_mob",[|
        "execute @s[tag=hasRareLoot] ~ ~ ~ function lorgon111:turn_on_rare_mob"
        """entitydata @s[tag=noRareLoot] {CustomName:"0",CustomNameVisible:1b}"""
        |]
    let turn_on_rare_mob = "turn_on_rare_mob",[|
        """summon armor_stand ~ ~ ~ {Small:0b,NoGravity:1b,Invisible:1b,Invulnerable:1b,Marker:1b,Tags:["rider","newAS"]}"""
        sprintf "scoreboard players add %s nextNum 1" FunctionCompiler.ENTITY_UUID
        sprintf "scoreboard players operation @s nextNum = %s nextNum" FunctionCompiler.ENTITY_UUID
        sprintf "scoreboard players operation @e[r=1,tag=newAS] nextNum = %s nextNum" FunctionCompiler.ENTITY_UUID
        "entitydata @s[tag=hasRareLoot] {Glowing:1b}"
        "execute @s[tag=hasDiamond] ~ ~ ~ entitydata @e[r=1,type=armor_stand,tag=newAS] {ArmorItems:[{},{},{},{id:diamond,Count:1b}]}"
        "execute @s[tag=hasApple] ~ ~ ~ entitydata @e[r=1,type=armor_stand,tag=newAS] {ArmorItems:[{},{},{},{id:apple,Count:1b}]}"
        "execute @s[tag=hasFlint] ~ ~ ~ entitydata @e[r=1,type=armor_stand,tag=newAS] {ArmorItems:[{},{},{},{id:flint,Count:1b}]}"
        "execute @s[tag=hasCake] ~ ~ ~ entitydata @e[r=1,type=armor_stand,tag=newAS] {ArmorItems:[{},{},{},{id:cake,Count:1b}]}"
        "scoreboard players tag @e[r=1,tag=newAS] remove newAS"
        |]
    let turn_off = "turn_off",[|
        """tellraw @a["foresight turned off"]"""
        """entitydata @e[tag=processed] {Glowing:0b,CustomName:"",CustomNameVisible:0b}"""
        "kill @e[type=armor_stand,tag=rider]"
        |]
    let summon25zombies = "summon25zombies",[|
        for i = 1 to 5 do
        for j = 1 to 5 do
        yield sprintf "execute @p ~%d ~ ~%d summon zombie" (i+2) (j+2)
        |]
    let summon400zombies = "summon400zombies",[|
        for i = 1 to 20 do
        for j = 1 to 20 do
        yield sprintf "execute @p ~%d ~ ~%d summon zombie" (i+2) (j+2)
        |]
    let summon100spiders = "summon100spiders",[|
        for i = 1 to 10 do
        for j = 1 to 10 do
        yield sprintf "execute @p ~%d ~ ~%d summon spider" (2*i+2) (2*j+2)
        |]
    let restart = "restart",[|
        "gamerule gameLoopFunction lorgon111:restart2"
        |]
    let restart2 = "restart2",[|
        "kill @e[type=!player]"
        "kill @e[type=!player]"
        "gamerule gameLoopFunction lorgon111:restart3"
        |]
    let restart3 = "restart3",[|
        "function lorgon111:foresight_init"
        "function lorgon111:prng_init"
        "gamerule gameLoopFunction lorgon111:foresight_loop"
        |]
    DropInModule("foresight_loop",oneTimeInit,[|
        foresight_loop
        process_zombies 
        process_zombie 
        process_spiders
        process_spider
        run_rider
        turn_on 
        turn_on_mob
        turn_on_rare_mob
        turn_off 
        summon25zombies
        summon400zombies
        summon100spiders
        restart
        restart2
        restart3
        |])

// Hm, an ench book as the loot could be good if you need to 'get close' to 'read its name' before kill maybe... requires toggling CustomNameVisible via commands though, ugh, weird in SMP
// actually, glowing guy with no item on his head can just be CustomNamed, and requires getting close to read name, e.g. "Feather Falling IV"

//////////////////////////////////////

// 'wait' drop-in module, to schedule something in the future (how work? queuing order? idempotency? ...)

(*
best idea: to schedule function F to run nTicks in future, summon armor_stand tagged 'F' and 'countdown' with score N
general runner SB removes 1 all [tag=countdown] guys
if score=0, then calls dispatch on those guys to read tags and call functions (1 place in system that known universe of functions; only run when time to callback)
-----
implement something simple first, atop those, e.g. a short music piece
-----
insta-enderpearl can become 'tractor beam' or 'grappling hook', put 10 AS along bresenham long axis at tenths, and every 2 ticks, TP the player to next AS
this would use delay alongside (within) CPS machine
-----
Then need to generalize CPS machine to multiple programs to dispatch (e.g. run mandelbrot and tractor beam at once); pump5 or whatever dispatches all live programs
Things like Yield and Stop now have to be per program (AS entity each program tracks), and Yielding would only occur when all AS are ready
And generalize Yield to WaitNTicksBeforeContinue or whatnot
 - Wait5 could be realized as a series of 5 basic blocks that do nothing and Yield (after I fix the bug where direct-tailcalls inline thru Yield), or
 - Wait5 could be realized as a loop that inits a var to 5, subtracts 1 each loop, and if >0 stays in loop or if =0 exits loop, and yields each iteration, yeah, can do that in 'user code' now
-----
Preemptive multitasking with lag-detection would do a fairness thing where every program runs one step/chunk before checking wall-clock, or whatnot
However there may be some programs that can't be pre-empted, so we might need to specify programs, or parts of programs, as cooperative only
What is right architecture/factoring for yield/wait/cooperate/preempt?
What is ideal programming model? This can wait, can focus on low-level.

Interleaving of multiple programs can create a shared-data problem.  Failure to interleave can create starvation or lack of fairness.  

There is no good way to 'lock' a portion of the world data; a mutex would cover the entire world.  That's probably untenable, unless we can guarantee that running
all the programs serially will still fit into a tick, in which case we can just serially run them and not worry about fairness since no one gets pre-empted.

I think the most practical thing is to is
 - each block of a program should have { MustNotYield: bool, MustWaitNTicks: int } Note that MWNT only makes sense to be non-zero if MNY is false
 - MustNotYield might refer to 'must run in this tick' or 'must not be interleaved'
 - the former can cause starvation anyway, so when such a process exists, run it EXCLUSIVELY until it's no longer in MustNotYield state
 - MustWaitNTicks is easy, each process has a variable that's counting down each time we start the pump, and if it's non-zero, we skip the process this tick
 - CanYield is where we have scheduling flexibility to deal with fairness
   - we need to measure system lag and have a policy of when to pre-empt
   - we need a scheduling policy of which process to run next; round-robin ensures progress, but may not be 'fair' if one process uses lots of CPU 
   - probably best to do RR, and maybe also somehow let the processes 'inquire' about recent CPU utilization so they can choose to yield or do work

So the logic for the dispatcher that continually loops in pump5 is like
    if not timeToPreempt then
        choose next round-robin process
        if MustWaitNTicks=0 then
            run one block (K blocks, for a suitable K that minimizes scheduler overhead while preserving liveness)
            while that process is in MustNotYield state
                run one block (not K, to avoid skipping ahead into a different MustNotYield section)
        else
            processesThatHaveWorkLeftThisTick--
        timeToPreempt <- computeLag || processesThatHaveWorkLeftThisTick==0
CPS unrolled, that becomes
    if MustNotYield then
        run one more block of current process
        update MustNotYield state
    else
        timeToPreempt <- computeLag || processesThatHaveWorkLeftThisTick==0
        if not timeToPreempt then
            choose next round-robin process
            if MustWaitNTicks=0 then
                run one block (K blocks, for a suitable K that minimizes scheduler overhead while preserving liveness)
                update MustNotYield state
            else
                processesThatHaveWorkLeftThisTick--
And at the start of pump1 root we subtract 1 from all processes with non-zero MustWaitNTicks values, and also do
    processesThatHaveWorkLeftThisTick <- MAX_PROC
so everyone has a shot to do work if they need it

Actually, "run one block" is "run K blocks" where K is a constant chosen to minimize scheduler overhead while preserving system liveness.  
Hmm, but also need to ensure e.g. that a system that alternates between MustNotYield blocks and CanYield blocks with K=2 doesn't get stuck in exclusion, so only run one block 
at a time once past K, or alternatively only run one block at a time in the MustNotYield loop.

Since the set of processes is known at 'compile time', we can assign each process a number, and hard-code the round-robin chooser to just be e.g. RR <- (RR+1) mod NumProcs
Actually, since each Proc has its own Must... variables, we need e.g. a global "makeProcessVariable" function in F# to make a Var for those, and since they are different
objectives in Minecraft, we need to hardcode/inline the round-robinness in the scheduler.  So when codegen happens, all P Processes are known, and the scheduler codegens inside
pump5 something akin to
    dispatcher code for proc1
    dispatcher code for proc2
    ...
    dispatcher code for procP
(and note that if all P processes are done, or if lag says time to pre-empt, dispatchers and the pump will all cancel out and yield to minecraft)

In terms of representation, I think each Process should just use its int ProcVar to be MustWaitNTicks, and a value of -1 represents MustNotYield.

When a process has no work left to do this tick, it MUST set MustWaitNTicks to non-zero, else it will busy wait in the scheduler.

If a process has no work for the foreseeable future, it can set MustWaitNTicks to an extremely large number.  An advancement or command block could then set it to 0 to wake it back up.
These are the moral equivalents of STOP and START; e.g. an implicit loop wrapped through entrypoint and exits of the program.

If there are programs that must run every tick and don't need the help of a pump (like ForesightPotion), then the gameLoopFunction can just be a function like
    function ForesightPotionLoop
    # any other of those style programs
    function pump1    # calls all programs requiring pumps for within-tick arbitrary loops/control, or programs that span ticks

Can detect and log process that is responsible for lagging server (e.g. can demo with unyielding mandelbrot, alongside foresight and snowball-teleport)

It might be a good policy to ensure every process gets at least one time slice each tick, if desired, hmm

Can have e.g. terrain gen put command blocks in chunks that detect idle cpu to wake up and do work a la gm4 (each chunk 1/10 change build struct, unless #ticks>K, then not)
-----
can I just have a chunkloader, and store all my entities in a loaded chunk out in the middle of nowhere? no more need spawn chunks
 - what chunkloader needs nothing to start? nothing
 - in worst case, can i summon AS at @p and spreadplayers it? (no, what if player in wrong dimension)
 - so i guess must have player in overworld to start a thingy? (also spreadplayers chunkloader fails if over void, hm)
 - ah, but execute-detect might work? seems to not any more... https://www.reddit.com/r/MinecraftCommands/comments/3r07sw/19_chunk_load_generator/
 - i guess can tp the player out there briefly? but then no way to tp him back (even if leave AS, it might unload)
 - ah, can just tp an entity out there, seems to load it.  so
    - summon entity at overworld player (or fail with error message)
    - tp entity far off
    - ... then, keep tping it there? have to try see what works to keep selection of the entity (@e[...]) alive, don't need other processing of chunk apart from selection
       - seems just constant tp not enough, nor constant spreadplayers... maybe need 5x5 chunks around to entity process? (ugh)
       - and then once working, actually do it at 0,0 so likely to be at spawn in practice and not loading extra useless chunks (though adds risk of not noticing chunk loader breaking)
(also, lookup all @e entities there using [x,y,z,dx,dy,dz] to ensure only that chunk searched)
*)

//////////////////////////////////////

// survey advancements for ideas of fun things (biomes?)

// snowball teleporter could turn into a grappling-hook that takes time to move over distance


//lava-dipped arrows (remote light/lava) "flowing_lava 7" does not seem to catch flammable things on fire, gives light for 1s and drops/disappears, or lava laser

// what would a UI for accessories/upgrades/trinkets/carrying-capacity/buy/sell look like? what are interesting non-armor/weapon upgrades? (arcane strike, various 'rush' buffs after kill/streak, ...)

//weapon with very long cooldown but very strong (e.g. one hit at start of battle?)

// something where if you look up at the sky, like fireworks-words appear or something? chest says look up, you do, particles spell something? all mobs look up? ...

//diamond-laying chickens under attack by zombie (how get Z to attack C?) you need to save if want to farm; 
// - or i guess chicken could be villager who produces goods with egg laying sound... 
// - could be fun recurring set piece thruout a map, find Z chasing C, save C and get a finite-farmable reward; people like to farm, this is non-standard farming


// one-way-home teleport mechanic? something time consuming you can't use in battle? drink potion, gives you nausea->blindness over 5s, then tp home

