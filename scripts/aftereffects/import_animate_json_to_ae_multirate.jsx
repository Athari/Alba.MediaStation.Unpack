/**
 * Adobe Animate JSON to After Effects Composition Importer (Multi-Rate & Audio Sync)
 * 
 * Enhanced companion importer script for Adobe After Effects CS6, CC 2014 through CC 2026+.
 * Compatible with ExtendScript (ECMAScript 3).
 * 
 * Enhancements:
 * 1. Per-Composition Native Frame Rates: Assigns each symbol pre-comp its native authoring
 *    frame rate (e.g. 10 fps, 5 fps, 8 fps, 12 fps) while keeping the Master Composition
 *    at screen/document rate (e.g. 60 fps).
 * 2. Synchronized Audio: Automatically imports associated .wav audio files for cutscene movies
 *    and places them on the movie pre-comp timeline in frame-accurate sync.
 * 3. Dynamic Canvas Dimensions: Derives default comp resolution from metadata (width/height)
 *    rather than hardcoding 1080p.
 * 4. Backward Compatibility: 100% compatible with standard Animate JSON files (falls back to
 *    document framerate and defaults if per-comp metadata is absent).
 * 
 * Supports both:
 * 1. Optimized short keys (AN, SD, MD, S, TL, L, FR, I, DU, E, SI, ASI, M3D, etc.)
 * 2. Unoptimized verbose keys (ANIMATION, SYMBOL_DICTIONARY, metadata, Symbols, TIMELINE, etc.)
 * 
 * Usage:
 * 1. In After Effects, navigate to: File > Scripts > Run Script File...
 * 2. Select this .jsx script.
 * 3. Choose your Animation.json file when prompted.
 */

(function (thisObj) {
    "use strict";

    // --- JSON Polyfill (for older ExtendScript engines without native JSON) ---
    var JSONParser = (typeof JSON !== "undefined" && JSON.parse) ? JSON : {
        parse: function (jsonStr) {
            return eval("(" + jsonStr + ")");
        }
    };

    // --- Helper to extract values supporting both short and long property keys ---
    function getVal(obj, keys, defaultVal) {
        if (!obj) return defaultVal;
        for (var i = 0; i < keys.length; i++) {
            if (obj[keys[i]] !== undefined && obj[keys[i]] !== null) {
                return obj[keys[i]];
            }
        }
        return defaultVal;
    }

    // --- Decomposes a column-major 4x4 matrix (16 floats) into 2D transforms ---
    function decomposeMatrix3D(m) {
        if (!m || m.length < 16) {
            return {
                x: 0,
                y: 0,
                scaleX: 100,
                scaleY: 100,
                rotation: 0
            };
        }

        // Column-major indices:
        // m[0] = a, m[1] = b, m[4] = c, m[5] = d
        // m[12] = tx, m[13] = ty
        var a = m[0];
        var b = m[1];
        var c = m[4];
        var d = m[5];
        var tx = m[12];
        var ty = m[13];

        var det = (a * d) - (b * c);
        var scaleX = Math.sqrt(a * a + b * b);
        var scaleY = Math.sqrt(c * c + d * d);

        // Account for reflection / negative scaling
        if (det < 0) {
            scaleY = -scaleY;
        }

        var rad = Math.atan2(b, a);
        var deg = rad * (180.0 / Math.PI);

        return {
            x: tx,
            y: ty,
            scaleX: scaleX * 100.0,
            scaleY: scaleY * 100.0,
            rotation: deg
        };
    }

    // --- Main Importer Class ---
    function AnimateImporter() {
        this.baseFolder = null;
        this.project = app.project;
        this.importedFootage = {}; // map: assetName -> FootageItem
        this.importedAudio = {};   // map: assetName -> FootageItem
        this.symbolComps = {};      // map: symbolName -> CompItem
    }

    AnimateImporter.prototype.run = function () {
        var jsonFile = File.openDialog("Select Adobe Animate Animation.json", "JSON:*.json;All Files:*.*");
        if (!jsonFile) return;

        this.baseFolder = jsonFile.parent;

        jsonFile.open("r");
        jsonFile.encoding = "UTF-8";
        var content = jsonFile.read();
        jsonFile.close();

        // Strip UTF-8 BOM if present
        if (content.charCodeAt(0) === 0xFEFF) {
            content = content.substring(1);
        }

        var animData;
        try {
            animData = JSONParser.parse(content);
        } catch (e) {
            alert("Error parsing JSON file:\n" + e.toString());
            return;
        }

        app.beginUndoGroup("Import Adobe Animate JSON (Multi-Rate)");

        try {
            this.processData(animData);
        } catch (err) {
            alert("An error occurred during import:\n" + err.toString() + "\nLine: " + (err.line || "unknown"));
        } finally {
            app.endUndoGroup();
        }
    };

    AnimateImporter.prototype.processData = function (animData) {
        var metadata = getVal(animData, ["metadata", "MD"], {});
        var docFps = getVal(metadata, ["framerate", "FRT"], 60.0);

        var anim = getVal(animData, ["ANIMATION", "AN"], {});
        var projectName = getVal(anim, ["name", "N"], "Animate_Import");
        var rootTimeline = getVal(anim, ["TIMELINE", "TL"], {});
        var symbolDict = getVal(animData, ["SYMBOL_DICTIONARY", "SD"], {});
        var rawSymbols = getVal(symbolDict, ["Symbols", "S"], []);

        // Load Spritemap metadata if present in same folder
        var spritemap = this.loadSpritemap();

        // 1. Create Project Folders for organization
        var rootFolder = this.project.items.addFolder(projectName + "_Assets");
        var compsFolder = this.project.items.addFolder(projectName + "_Compositions");

        // 2. Pre-import all referenced sprites and audio files
        this.preloadSprites(spritemap, rootFolder);

        // 3. Calculate canvas dimensions & symbol lengths
        var symbolLengths = {};
        for (var s = 0; s < rawSymbols.length; s++) {
            var sym = rawSymbols[s];
            var symName = getVal(sym, ["SYMBOL_name", "SN"], "Symbol_" + s);
            var symTl = getVal(sym, ["TIMELINE", "TL"], {});
            symbolLengths[symName] = this.getTimelineLength(symTl);
        }
        var rootLength = this.getTimelineLength(rootTimeline);
        if (rootLength <= 0) rootLength = 1;

        // Default dimensions from metadata or spritemap (fallback 640x480)
        var defaultWidth = getVal(metadata, ["width", "W"], 640);
        var defaultHeight = getVal(metadata, ["height", "H"], 480);
        if (spritemap && spritemap.meta && spritemap.meta.size) {
            defaultWidth = spritemap.meta.size.w;
            defaultHeight = spritemap.meta.size.h;
        }

        // 4. Create Pre-Compositions for all symbols in the dictionary with native FPS
        for (var s = 0; s < rawSymbols.length; s++) {
            var sym = rawSymbols[s];
            var symName = getVal(sym, ["SYMBOL_name", "SN"], "Symbol_" + s);
            var symTl = getVal(sym, ["TIMELINE", "TL"], {});
            var symFps = getVal(sym, ["framerate", "FRT"], docFps);
            var duration = Math.max(1, symbolLengths[symName]) / docFps;

            var comp = this.project.items.addComp(symName, defaultWidth, defaultHeight, 1.0, duration, symFps);
            comp.parentFolder = compsFolder;
            this.symbolComps[symName] = comp;
        }

        // 5. Populate symbol compositions & link synchronized audio
        for (var s = 0; s < rawSymbols.length; s++) {
            var sym = rawSymbols[s];
            var symName = getVal(sym, ["SYMBOL_name", "SN"], "Symbol_" + s);
            var symTl = getVal(sym, ["TIMELINE", "TL"], {});
            var comp = this.symbolComps[symName];
            this.buildTimeline(comp, symTl, docFps);

            // Add audio layer if audio file exists for this symbol (e.g. Movie_Intro.wav)
            var audioFootage = this.importedAudio[symName] || this.importedAudio[symName + ".wav"];
            if (audioFootage) {
                try {
                    var audioLayer = comp.layers.add(audioFootage);
                    audioLayer.name = symName + " [Audio]";
                    audioLayer.inPoint = 0;
                    audioLayer.moveToEnd();
                } catch (audioErr) {}
            }
        }

        // 6. Build the Master Root Composition (at document screen rate)
        var rootDuration = Math.max(1, rootLength) / docFps;
        var masterComp = this.project.items.addComp(projectName + "_Master", defaultWidth, defaultHeight, 1.0, rootDuration, docFps);
        masterComp.parentFolder = compsFolder;
        this.buildTimeline(masterComp, rootTimeline, docFps);

        // Open Master Comp in Viewer
        masterComp.openInViewer();
    };

    // --- Calculates the maximum frame count across all layers in a timeline ---
    AnimateImporter.prototype.getTimelineLength = function (tl) {
        var layers = getVal(tl, ["LAYERS", "L"], []);
        var maxFrames = 0;
        for (var i = 0; i < layers.length; i++) {
            var frames = getVal(layers[i], ["Frames", "FR"], []);
            for (var f = 0; f < frames.length; f++) {
                var fr = frames[f];
                var index = getVal(fr, ["index", "I"], 0);
                var duration = getVal(fr, ["duration", "DU"], 1);
                if (index + duration > maxFrames) {
                    maxFrames = index + duration;
                }
            }
        }
        return maxFrames;
    };

    // --- Loads spritemap.json if it exists in the folder ---
    AnimateImporter.prototype.loadSpritemap = function () {
        var mapFile = new File(this.baseFolder.fsName + "/spritemap1.json");
        if (!mapFile.exists) {
            mapFile = new File(this.baseFolder.fsName + "/spritemap.json");
        }
        if (!mapFile.exists) return null;

        mapFile.open("r");
        mapFile.encoding = "UTF-8";
        var content = mapFile.read();
        mapFile.close();

        if (content.charCodeAt(0) === 0xFEFF) {
            content = content.substring(1);
        }

        try {
            return JSONParser.parse(content);
        } catch (e) {
            return null;
        }
    };

    // --- Preloads image and audio files into Project panel ---
    AnimateImporter.prototype.preloadSprites = function (spritemap, folder) {
        var spriteDir = new Folder(this.baseFolder.fsName + "/sprites");
        var searchFolder = spriteDir.exists ? spriteDir : this.baseFolder;

        // Option A: Loose PNG / raster images
        var imageFiles = searchFolder.getFiles(function (f) {
            return f instanceof File && f.name.match(/\.(png|jpg|jpeg|tga)$/i);
        });

        for (var i = 0; i < imageFiles.length; i++) {
            var file = imageFiles[i];
            var baseName = file.name.replace(/\.[^\.]+$/, "");
            try {
                var io = new ImportOptions(file);
                var footage = this.project.importFile(io);
                footage.parentFolder = folder;
                this.importedFootage[baseName] = footage;
                this.importedFootage[file.name] = footage;
            } catch (err) {}
        }

        // Option B: Spritemap atlas PNG
        var atlasPng = new File(this.baseFolder.fsName + "/spritemap1.png");
        if (!atlasPng.exists) atlasPng = new File(this.baseFolder.fsName + "/spritemap.png");
        if (atlasPng.exists) {
            try {
                var ioAtlas = new ImportOptions(atlasPng);
                var footageAtlas = this.project.importFile(ioAtlas);
                footageAtlas.parentFolder = folder;
                this.importedFootage["__ATLAS__"] = footageAtlas;
            } catch (err) {}
        }

        // Option C: Audio files (.wav, .mp3, .aif, .aiff)
        var audioFiles = searchFolder.getFiles(function (f) {
            return f instanceof File && f.name.match(/\.(wav|mp3|aif|aiff)$/i);
        });

        for (var a = 0; a < audioFiles.length; a++) {
            var aFile = audioFiles[a];
            var aBaseName = aFile.name.replace(/\.[^\.]+$/, "");
            try {
                var aIo = new ImportOptions(aFile);
                var aFootage = this.project.importFile(aIo);
                aFootage.parentFolder = folder;
                this.importedAudio[aBaseName] = aFootage;
                this.importedAudio[aFile.name] = aFootage;
            } catch (aErr) {}
        }
    };

    // --- Builds layers, keyframes, and timing inside a composition ---
    AnimateImporter.prototype.buildTimeline = function (comp, timeline, docFps) {
        var rawLayers = getVal(timeline, ["LAYERS", "L"], []);
        if (!rawLayers || rawLayers.length === 0) return;

        // Layers ordered top-to-bottom: iterate reverse so lower layers are added first
        for (var l = rawLayers.length - 1; l >= 0; l--) {
            var layerData = rawLayers[l];
            var layerName = getVal(layerData, ["Layer_name", "LN"], "Layer_" + (l + 1));
            var frames = getVal(layerData, ["Frames", "FR"], []);
            if (!frames || frames.length === 0) continue;

            this.buildLayerFrames(comp, layerName, frames, docFps);
        }
    };

    // --- Builds elements across keyframes on a specific layer ---
    AnimateImporter.prototype.buildLayerFrames = function (comp, layerName, frames, docFps) {
        for (var f = 0; f < frames.length; f++) {
            var frame = frames[f];
            var frameIndex = getVal(frame, ["index", "I"], 0);
            var duration = getVal(frame, ["duration", "DU"], 1);
            var elements = getVal(frame, ["elements", "E"], []);

            var startTime = frameIndex / docFps;
            var endTime = (frameIndex + duration) / docFps;

            for (var e = 0; e < elements.length; e++) {
                var elem = elements[e];

                var si = getVal(elem, ["SYMBOL_Instance", "SI"], null);
                var asi = getVal(elem, ["ATLAS_SPRITE_instance", "ASI"], null);

                var targetLayer = null;

                if (si) {
                    var symName = getVal(si, ["SYMBOL_name", "SN"], "");
                    var subComp = this.symbolComps[symName];
                    if (subComp) {
                        targetLayer = comp.layers.add(subComp);
                        targetLayer.name = symName + " [" + layerName + "]";
                    }
                } else if (asi) {
                    var spriteName = getVal(asi, ["name", "N"], "");
                    var footage = this.importedFootage[spriteName] || this.importedFootage["__ATLAS__"];
                    if (footage) {
                        targetLayer = comp.layers.add(footage);
                        targetLayer.name = spriteName + " [" + layerName + "]";
                    }
                }

                if (!targetLayer) continue;

                // Set in/out times in seconds
                targetLayer.inPoint = startTime;
                targetLayer.outPoint = endTime;

                var m3d = null;
                var trp = null;
                var colorEffect = null;

                if (si) {
                    m3d = getVal(si, ["Matrix3D", "M3D"], null);
                    trp = getVal(si, ["transformationPoint", "TRP"], null);
                    colorEffect = getVal(si, ["color", "C"], null);
                } else if (asi) {
                    m3d = getVal(asi, ["Matrix3D", "M3D"], null);
                    trp = getVal(asi, ["Position", "POS"], null);
                }

                // Apply Anchor Point
                if (trp) {
                    var ax = trp.x || 0;
                    var ay = trp.y || 0;
                    targetLayer.property("Anchor Point").setValue([ax, ay]);
                }

                // Decompose and apply 2D transforms with Hold Keyframes
                if (m3d) {
                    var xform = decomposeMatrix3D(m3d);

                    var posProp = targetLayer.property("Position");
                    var scaleProp = targetLayer.property("Scale");
                    var rotProp = targetLayer.property("Rotation");

                    posProp.setValueAtTime(startTime, [xform.x, xform.y]);
                    scaleProp.setValueAtTime(startTime, [xform.scaleX, xform.scaleY]);
                    rotProp.setValueAtTime(startTime, xform.rotation);

                    this.setHoldKeyframe(posProp, startTime);
                    this.setHoldKeyframe(scaleProp, startTime);
                    this.setHoldKeyframe(rotProp, startTime);
                }

                // Apply Color / Alpha Effect
                if (colorEffect) {
                    var alpha = getVal(colorEffect, ["alphaMultiplier", "AM"], 1.0);
                    if (alpha !== 1.0) {
                        var opProp = targetLayer.property("Opacity");
                        opProp.setValueAtTime(startTime, alpha * 100.0);
                        this.setHoldKeyframe(opProp, startTime);
                    }
                }
            }
        }
    };

    // --- Helper to set a property keyframe to HOLD interpolation ---
    AnimateImporter.prototype.setHoldKeyframe = function (prop, time) {
        if (!prop || !prop.setValueAtTime) return;
        try {
            var kfIndex = prop.nearestKeyIndex(time);
            prop.setInterpolationTypeAtKey(kfIndex, KeyframeInterpolationType.HOLD);
        } catch (e) {}
    };

    // --- Execute Importer ---
    var importer = new AnimateImporter();
    importer.run();

})(this);
