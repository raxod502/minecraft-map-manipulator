﻿module RecomputeLighting

// recompute the BlockLight/SkyLight/HeightMap/LightPopulated values after making changes

// TODO how to do large sections that don't all fit in memory at once?
//  - compute e.g. chunk 0,0 alone, then compute chunk 0,1 using the boundary values from 0,0 as sources
//      - but this won't find light that 'goes around the corner from 0,1 into 0,0 and then back into 0,1', will it? could repeat entire chunk (16 blocks far enough can't propagate)
//      - had a discussion with codewarrior; for now i think i will just redo one chunk at the boundary. this lends towards large scale work (more volume/less surface means less re-work at boundaries), but beware of tension that cache/locality get poorer at large scale (e.g. my hashset of 15-sources is walking all over memory)
//      - can't just 'redo from scratch' tho, or will miss light coming in from completed side.  need to use known-correct wall as outside sources too...
//  - then can test how long it takes to compute light of whole region at different 'chunking' sizes
//  - and can test for correctness against a saved 'good' copy

// need to be careful that slabs/stairs/farmland/?path? are opaque but have self light-level of neighbor

let INTRINSIC_BRIGHTNESS =
    let a = Array.zeroCreate 256
    for bid, ib in MC_Constants.BLOCKIDS_THAT_EMIT_LIGHT do
        a.[bid] <- ib
    a

let brightness(bid) = INTRINSIC_BRIGHTNESS.[bid]

let OPACITY =
    let a = Array.create 256 15
    for bid in MC_Constants.BLOCKIDS_THAT_ARE_FULLY_TRANSPARENT_TO_LIGHT do
        a.[bid] <- 1
    for bid in MC_Constants.BLOCKIDS_THAT_FILTER_SKYLIGHT do
        a.[bid] <- 1
    for bid in MC_Constants.BLOCKIDS_THAT_LOWER_LIGHT_BY_TWO do
        a.[bid] <- 3
    a

let opacity(bid) = OPACITY.[bid]

open RegionFiles

// this is an additive, batch-repainting-of-sources
let recomputeLightCore(map:MapFolder, canChange, sourcesByLevel:System.Collections.Generic.HashSet<_>[], isSky) =
    // propogate light at each level
    for level = 15 downto 1 do
        printfn "There are %d sources at level %d" sourcesByLevel.[level].Count level
        for x,y,z in sourcesByLevel.[level] do
            let _nbt,_bids,_blockData,blockLight,skyLight = map.GetSection(x,y,z)
            let light = if isSky then skyLight else blockLight
            let curLight = NibbleArray.get(light,x,y,z)
            assert(curLight = byte level)
            for dx,dy,dz in [| 0,0,1; 0,1,0; 1,0,0; 0,0,-1; 0,-1,0; -1,0,0 |] do
                if canChange(x+dx,y+dy,z+dz) then
                    let neighborBID = map.GetBlockInfo(x+dx,y+dy,z+dz).BlockID 
                    let neighborLevelBasedOnMySpread = max 0 (level - OPACITY.[int neighborBID])
                    assert(neighborLevelBasedOnMySpread < level)
                    if neighborLevelBasedOnMySpread > 0 then
                        let _,_,_,neighborBlockLight,neighborSkyLight = map.GetSection(x+dx,y+dy,z+dz)
                        let neighborLight = if isSky then neighborSkyLight else neighborBlockLight
                        let curNeighborLevel = NibbleArray.get(neighborLight,x+dx,y+dy,z+dz)
                        if curNeighborLevel < byte neighborLevelBasedOnMySpread then
                            NibbleArray.set(neighborLight,x+dx,y+dy,z+dz,byte neighborLevelBasedOnMySpread)
                            sourcesByLevel.[neighborLevelBasedOnMySpread].Add(x+dx,y+dy,z+dz) |> ignore
    ()
let recomputeBlockLightHelper(map:MapFolder, canChange, blockLightSources) = // blockLightSources is coords of all light-emitting blocks
    // TODO assert all blockLightSources have correct non-zero value
    recomputeLightCore(map, canChange, blockLightSources, false)
let recomputeSkyLightHelper(map:MapFolder, canChange, skyLightSources) = // skyLightSources is coords of all blocks from ceiling down to last _fully_ transparent block
    // TODO wiki suggests leaves/cobweb/ice are different, but I cannot find anything that suggests I need to handle them differently
    recomputeLightCore(map, canChange, skyLightSources, true)

let LIGHTBIDS = new System.Collections.Generic.Dictionary<_,_>()
for bid, lvl in MC_Constants.BLOCKIDS_THAT_EMIT_LIGHT do
    LIGHTBIDS.Add(byte bid, byte lvl)

let recomputeBlockLight(map:MapFolder,sourcesByLevel:System.Collections.Generic.HashSet<_>[],minx,minz,maxx,maxz,canChange,shouldZero) =
    if MOD(minx,16) <> 0 || MOD(maxx,16) <> 15 || MOD(minz,16) <> 0 || MOD(maxz,16) <> 15 then
        failwith "this algorithm only works on full chunks"
    printf "finding light sources and initting light to 0..."
    // loop over every represented block, section-wise (section-wise was more than 3x faster when I benchmarked it)
    for xs in [ minx .. 16 .. maxx ] do
        for zs in [ minz .. 16 .. maxz ] do
            for ys in [ 0 .. 16 .. 255 ] do
                printf "."
                let bi = map.MaybeGetBlockInfo(xs,ys,zs)
                if bi <> null then
                    let _,blocks,_,blockLight,_ = map.GetSection(xs,ys,zs)
                    if shouldZero then
                        // zero out the light values to begin
                        System.Array.Clear(blockLight, 0, 2048)
                    for dx = 0 to 15 do
                        for dy = 0 to 15 do
                            for dz = 0 to 15 do
                                //let bid = map.GetBlockInfo(xs+dx,ys+dy,zs+dz).BlockID   // could use this API, but below is faster since we already have section
                                let i = dy*256 + dz*16 + dx
                                let bid = blocks.[i]
                                // if it emits light, add it to light sources
                                if LIGHTBIDS.ContainsKey(bid) then
                                    let level = LIGHTBIDS.[bid]
                                    if shouldZero then // it's definitely a source we need to track
                                        sourcesByLevel.[int level].Add(xs+dx,ys+dy,zs+dz) |> ignore
                                        NibbleArray.set(blockLight,xs+dx,ys+dy,zs+dz,level)
                                    else // we may have already painted a higher level over this source (from a source outside this chunk/section), in which case this source can be ignored, so check it
                                        let existingLevel = NibbleArray.get(blockLight,xs+dx,ys+dy,zs+dz)
                                        if existingLevel < level then
                                            sourcesByLevel.[int level].Add(xs+dx,ys+dy,zs+dz) |> ignore
                                            NibbleArray.set(blockLight,xs+dx,ys+dy,zs+dz,level)
    printfn "done!"
    printfn "recomputing light..."
    // recompute block light
    recomputeBlockLightHelper(map, canChange, sourcesByLevel)

let recomputeSkyLight(map:MapFolder,sourcesByLevel:System.Collections.Generic.HashSet<_>[],cachedHeightMap:_[,],minx,minz,maxx,maxz,canChange,shouldZero) =
    let chmminx, chmminz = cachedHeightMap.GetLowerBound(0), cachedHeightMap.GetLowerBound(1)
    let chmmaxx, chmmaxz = chmminx+cachedHeightMap.GetLength(0)-1, chmminz+cachedHeightMap.GetLength(1)-1
    printf "finding light sources and initting light to 0..."
    let NINE_NEIGHBORS = [| -1,-1; -1,0; -1,1; 0,-1; 0,0; 0,1; 1,-1; 1,0; 1,1 |]
    // for every represented block
    for xs in [ minx .. 16 .. maxx ] do
        for zs in [ minz .. 16 .. maxz ] do
            printf "."
            let mutable highestHMInThisChunk,lowestHMInThisChunk = 0,255
            for dx = 0 to 15 do
                for dz = 0 to 15 do
                    let x,z = xs+dx,zs+dz
                    if cachedHeightMap.[x,z] > highestHMInThisChunk then
                        highestHMInThisChunk <- cachedHeightMap.[x,z]
                    if cachedHeightMap.[x,z] < lowestHMInThisChunk then
                        lowestHMInThisChunk <- cachedHeightMap.[x,z]
            let mutable ys = 240
            while highestHMInThisChunk < ys-1 do  // -1 because I need to represent skylight source above the highest block
                // this section is entirely above the heightmap; it could be unrepresented, or if represented, its skyLight is just all 15s
                let _,_,_,_,skyLight = map.GetSection(xs,ys,zs)
                if skyLight <> null then
                    for i = 0 to skyLight.Length-1 do
                        skyLight.[i] <- 255uy  // 255 is (15 <<< 4) + 15
                ys <- ys - 16
            if ys = highestHMInThisChunk then // special-case this for efficiency - all sky, but with terrain adjacent below; just want to ensure is represented
                let _,_,_,_,skyLight = map.GetOrCreateSection(xs,ys,zs)
                for i = 0 to skyLight.Length-1 do
                    skyLight.[i] <- 255uy  // 255 is (15 <<< 4) + 15
                ys <- ys - 16
            // now the meat, we're into sections with some bits below the heightmap
            while ys >=0 do
                let _,_,_,_,skyLight = map.GetOrCreateSection(xs,ys,zs) // represent all sections below heightmap
                for dx = 0 to 15 do
                    for dz = 0 to 15 do
                        let x,z = xs+dx,zs+dz
                        let curHM = cachedHeightMap.[x,z]
                        for dy = 0 to 15 do
                            let y = ys+dy
                            if y >= curHM then
                                NibbleArray.set(skyLight,x,y,z,15uy)
                                // we don't need to set _every_ block in the sky as a light source; there's tons of sky that's all sources
                                // as a result, only start setting sources as we reach terrain
                                let mutable terrainNearby = false
                                for dx,dz in NINE_NEIGHBORS do
                                    let x,z = x+dx, z+dz
                                    if x >= chmminx && x <= chmmaxx && z >= chmminz && z <= chmmaxz then // if we can't read at the edge, then some overlapped processing will be needed for correctness when e.g. skylight is here and overhang is just outside readable data.
                                        if y <= cachedHeightMap.[x,z]+1 then
                                            terrainNearby <- true
                                if terrainNearby then
                                    sourcesByLevel.[15].Add(x,y,z) |> ignore
                            else
                                if shouldZero then
                                    NibbleArray.set(skyLight,x,y,z,0uy) // init all non-sources to 0
                ys <- ys - 16
    printfn "done!"
    printfn "recomputing light..."
    // recompute sky light
    recomputeSkyLightHelper(map, canChange, sourcesByLevel)

let compareLighting(map1:MapFolder, map2:MapFolder, minx, minz, maxx, maxz) =
    if MOD(minx,16) <> 0 || MOD(maxx,16) <> 15 || MOD(minz,16) <> 0 || MOD(maxz,16) <> 15 then
        failwith "this algorithm only works on full chunks"
    printfn "comparing results..."
    // compare
    let mutable numBlockDiff,numSkyDiff = 0,0
    for xs in [ minx .. 16 .. maxx ] do
        for zs in [ minz .. 16 .. maxz ] do
            for ys in [ 0 .. 16 .. 255 ] do
                printf "."
                let _,_,_,origBlockLight,origSkyLight = map1.GetSection(xs,ys,zs)
                if origBlockLight <> null then
                    let _,_,_,newBlockLight,newSkyLight = map2.GetSection(xs,ys,zs)
                    for dx = 0 to 15 do
                        for dy = 0 to 15 do
                            for dz = 0 to 15 do
                                let x = xs+dx
                                let y = ys+dy
                                let z = zs+dz
                                let origValue = NibbleArray.get(origBlockLight,x,y,z)
                                let testValue = NibbleArray.get(newBlockLight,x,y,z)
                                if origValue <> testValue then
                                    printfn "%3d %3d %3d differ, orig %2d test %2d" x y z origValue testValue 
                                    numBlockDiff <- numBlockDiff + 1
                                let origValue = NibbleArray.get(origSkyLight,x,y,z)
                                let testValue = NibbleArray.get(newSkyLight,x,y,z)
                                if origValue <> testValue then
                                    printfn "%3d %3d %3d differ, orig %2d test %2d" x y z origValue testValue 
                                    numSkyDiff <- numSkyDiff + 1
    printfn "done!"
    printfn "There were %d block and %d sky differences" numBlockDiff numSkyDiff

let fixLighting(mapToChange:MapFolder, 
                blockLightSourcesByLevel:System.Collections.Generic.HashSet<_>[], 
                skyLightSourcesByLevel:System.Collections.Generic.HashSet<_>[], 
                minx, minz, maxx, maxz, hm, canChange,
                shouldZero) =
    // blockLightSourcesByLevel, skyLightSourcesByLevel : any sources outside [(minx,minz) - (maxx,maxz)] that should shine light in
    // minx,minz,maxx,maxz                              : range of blocks to (possible zero and) scan for sources and shine light from
    // hm                                               : a cached heightmap of the area, at least the size of, and ideally at least one cell wider around than, [(minx,minz) - (maxx,maxz)]
    // canChange                                        : function saying which cells we can write new light values to (could e.g. be one-chunk-border-bigger-than [(minx,minz) - (maxx,maxz)])
    // shouldZero                                       : whether [(minx,minz) - (maxx,maxz)] light should be zeroed out at the start (e.g. because computing from scratch or removing/dimming, as opposed to incrementally adding)
    if MOD(minx,16) <> 0 || MOD(maxx,16) <> 15 || MOD(minz,16) <> 0 || MOD(maxz,16) <> 15 then
        failwith "this algorithm only works on full chunks"
    printfn "loading map..."
    mapToChange.GetOrCreateAllSections(minx,maxx,0,255,minz,maxz)
    printfn "recomputing block light..."
    recomputeBlockLight(mapToChange, blockLightSourcesByLevel, minx, minz, maxx, maxz, canChange, shouldZero)
    printfn "recomputing sky light..."
    recomputeSkyLight(mapToChange, skyLightSourcesByLevel, hm, minx, minz, maxx, maxz, canChange, shouldZero)

let lightingTestSetup() =
    let sampleRegionFolder = """C:\Users\Admin1\AppData\Roaming\.minecraft\saves\RCTM109\region\"""
    let originalRegionFolder = """C:\Users\Admin1\AppData\Roaming\.minecraft\saves\RCTM109OriginalLighting\region\"""
    let fixedRegionFolder = """C:\Users\Admin1\AppData\Roaming\.minecraft\saves\RCTM109CorrectedLighting\region\"""
    System.IO.Directory.CreateDirectory(originalRegionFolder) |> ignore
    System.IO.Directory.CreateDirectory(fixedRegionFolder) |> ignore
    System.IO.File.Copy(sampleRegionFolder+"r.0.0.mca", originalRegionFolder+"r.0.0.mca", true)
    System.IO.File.Copy(sampleRegionFolder+"r.0.0.mca", fixedRegionFolder+"r.0.0.mca", true)

    let minx, minz, maxx, maxz = 0, 0, 511, 511

    let mapToChange = new MapFolder(fixedRegionFolder)
    // cache height map
    let hm = Array2D.zeroCreateBased minx minz (maxx-minx+1) (maxz-minz+1)
    for x = minx to maxx do
        for z = minz to maxz do
            if MOD(x,16)=0 && MOD(z,16)=0 then
                mapToChange.GetBlockInfo(x,0,z) |> ignore // originally caches HeightMap, which is stored as arrays per-chunk
            hm.[x,z] <- mapToChange.GetHeightMap(x,z)
    let canChange(x,y,z) = x >= minx && x <= maxx && y >= 0 && y <= 255 && z >= minz && z <= maxz
    let blockLightSourcesByLevel = Array.init 16 (fun _ -> new System.Collections.Generic.HashSet<_>())
    let skyLightSourcesByLevel = Array.init 16 (fun _ -> new System.Collections.Generic.HashSet<_>())
    fixLighting(mapToChange, blockLightSourcesByLevel, skyLightSourcesByLevel, minx, minz, maxx, maxz, hm, canChange, true)
    printfn "saving results..."
    mapToChange.WriteAll()
    // compare
    let origMap = new MapFolder(originalRegionFolder)
    compareLighting(origMap, mapToChange, minx, minz, maxx, maxz)

let demoCorrectBoundaries() =
    let originalRegionFolder = """C:\Users\Admin1\AppData\Roaming\.minecraft\saves\RCTM109OriginalLighting\region\"""
    let fixedRegionFolder = """C:\Users\Admin1\AppData\Roaming\.minecraft\saves\RCTM109CorrectedLighting\region\"""

    let minx, minz, maxx, maxz = 0, 0, 511, 511

    let mapToChange = new MapFolder(originalRegionFolder)
    // cache height map
    let hm = Array2D.zeroCreateBased minx minz (maxx-minx+1) (maxz-minz+1)
    for x = minx to maxx do
        for z = minz to maxz do
            if MOD(x,16)=0 && MOD(z,16)=0 then
                mapToChange.GetBlockInfo(x,0,z) |> ignore // originally caches HeightMap, which is stored as arrays per-chunk
            hm.[x,z] <- mapToChange.GetHeightMap(x,z)
    let canChange(x,y,z) = x >= minx && x <= maxx && y >= 0 && y <= 255 && z >= minz && z <= maxz

    (*
    let blockLightSourcesByLevel = Array.init 16 (fun _ -> new System.Collections.Generic.HashSet<_>())
    let skyLightSourcesByLevel = Array.init 16 (fun _ -> new System.Collections.Generic.HashSet<_>())
    fixLighting(mapToChange, blockLightSourcesByLevel, skyLightSourcesByLevel, minx, minz, maxx, maxz, true)
    *)

    let sw = System.Diagnostics.Stopwatch.StartNew()
// TODO test different size chunking for performance and correctness
    let MAX = 512
    let PARTS = 8
    let LEN = MAX / PARTS
    let hasBeenZeroed = new System.Collections.Generic.HashSet<_>()
    for x = 0 to PARTS-1 do
        for z = 0 to PARTS-1 do
            let blockLightSourcesByLevel = Array.init 16 (fun _ -> new System.Collections.Generic.HashSet<_>())
            let skyLightSourcesByLevel = Array.init 16 (fun _ -> new System.Collections.Generic.HashSet<_>())
            // zero out all lighting in this chunking-area... and also one chunk in the forward X/Z directions (the +16)
            for xs in [x*LEN .. 16 .. x*LEN+LEN-1+16] do
                for zs in [z*LEN .. 16 .. z*LEN+LEN-1+16] do
                    if xs<MAX && zs<MAX then
                        for ys in [0 .. 16 .. 255] do
                            if not(hasBeenZeroed.Contains(xs,ys,zs)) then
                                let _,_,_,blockLight,skyLight = mapToChange.GetSection(xs,ys,zs)
                                if blockLight <> null then
                                    System.Array.Clear(blockLight, 0, 2048)
                                    System.Array.Clear(skyLight, 0, 2048)
                                    hasBeenZeroed.Add(xs,ys,zs) |> ignore
            fixLighting(mapToChange,blockLightSourcesByLevel,skyLightSourcesByLevel,x*LEN,z*LEN,x*LEN+LEN-1,z*LEN+LEN-1,hm,canChange,false)
    printfn "took %dms" sw.ElapsedMilliseconds 
    // compare
    let fixedMap = new MapFolder(fixedRegionFolder)
    compareLighting(fixedMap, mapToChange, minx, minz, maxx, maxz)




(*

blocklight:

have set of 'could be changed by update' (easy to compute at start).  
all blocks with intrinsic light values there are noted, as well as all points on the surface boundary, as light 'sources' with strength N

then for N = 15 downto 0 do

forall sources with strength N, set block light to N (unless already higher, in which case stop), 
then add all neighbors to lists of N-minus-their-opacity sources (unless they were already brighter)

thus we have the 15 wavefront, then the 14 wavefront, ... no work is done for e.g. red torches until we reach iter 7

each block is only ever set once

-----------------------

the 'could be changed by update' set is useful for a single change or a set of changes
to recompute entire chunk/region at once, probably best to just do everything, e.g. the 'could be changed' is 'everything'
hm, do we need the 'could be changed' set? does the wavefront implicitly do this?
aha, yes, we need if for dimming - when light is dimmed or removed (includes e.g. putting stone to replace air), we need to
 - compute the changed set (the N-radius around the N-light-removed block)
 - note its frontier (set on the boundary/edge of the region)
 - after unioning all the changed sets from all the changes, union the frontiers and then for each guy on the frontier,
     check all his neighbors for blocks outside the change scope, these become the new 'sources' that will bleed in

-----------------------

skylight is similar to blocklight
any time a block is placed above HM, need to fix HM, and also ensure sections exist from bottom up to that block, recompute
any time a block is removed at HM, need to fix HM, recompute

*)


