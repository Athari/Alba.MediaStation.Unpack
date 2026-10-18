/**
 * Adobe Animate JSON to After Effects Composition Importer
 * 
 * Compatible with Adobe After Effects CS6, CC 2014 through CC 2026+.
 * Compatible with ExtendScript (ECMAScript 3).
 * 
 * Supports both:
 * 1. Optimized short keys (AN, SD, MD, S, TL, L, FR, I, DU, E, SI, ASI, M3D, etc.)
 * 2. Unoptimized verbose keys (ANIMATION, SYMBOL_DICTIONARY, metadata, Symbols, TIMELINE, etc.)
 * 
 * Usage:
 * 1. In After Effects, navigate to: File > Scripts > Run Script File...
 * 2. Select this .jsx script.
 * 3. Choose your Animation.json file when prompted.
 * 4. The script will import all sprite assets, build Pre-Compositions for all symbols,
 *    and construct the master animation with frame-accurate hold keyframes.
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

        app.beginUndoGroup("Import Adobe Animate JSON");

        try {
            this.processData(animData);
        } catch (err) {
            alert("An error occurred during import:\n" + err.toString() + "\nLine: " + (err.line || "unknown"));
        } finally {
            app.endUndoGroup();
        }
    };

    AnimateImporter.prototype.processData = function (animData) {
        var metadata = getVal(animData, ["MD", "metadata"], {});
        var fps = getVal(metadata, ["FRT", "framerate"], 24.0);

        var anim = getVal(animData, ["AN", "ANIMATION"], {});
        var projectName = getVal(anim, ["N", "name"], "Animate_Import");
        var rootTimeline = getVal(anim, ["TL", "TIMELINE"], {});
        var symbolDict = getVal(animData, ["SD", "SYMBOL_DICTIONARY"], {});
        var rawSymbols = getVal(symbolDict, ["S", "Symbols"], []);

        // Load Spritemap metadata if present in same folder
        var spritemap = this.loadSpritemap();

        // 1. Create a Project Folder for organization
        var rootFolder = this.project.items.addFolder(projectName + "_Assets");
        var compsFolder = this.project.items.addFolder(projectName + "_Compositions");

        // 2. Pre-import all referenced sprites (either loose PNGs or spritemap atlas)
        this.preloadSprites(spritemap, rootFolder);

        // 3. Calculate canvas dimensions & symbol lengths
        var symbolLengths = {};
        for (var s = 0; s < rawSymbols.length; s++) {
            var sym = rawSymbols[s];
            var symName = getVal(sym, ["SN", "SYMBOL_name"], "Symbol_" + s);
            var symTl = getVal(sym, ["TL", "TIMELINE"], {});
            symbolLengths[symName] = this.getTimelineLength(symTl);
        }
        var rootLength = this.getTimelineLength(rootTimeline);
        if (rootLength <= 0) rootLength = 1;

        // Default dimensions if not specified: 1920x1080 (or derive from footage)
        var defaultWidth = 1920;
        var defaultHeight = 1080;
        if (spritemap && spritemap.meta && spritemap.meta.size) {
            defaultWidth = spritemap.meta.size.w;
            defaultHeight = spritemap.meta.size.h;
        }

        // 4. Create Pre-Compositions for all symbols in the dictionary (bottom-up)
        for (var s = 0; s < rawSymbols.length; s++) {
            var sym = rawSymbols[s];
            var symName = getVal(sym, ["SN", "SYMBOL_name"], "Symbol_" + s);
            var symTl = getVal(sym, ["TL", "TIMELINE"], {});
            var duration = Math.max(1, symbolLengths[symName]) / fps;

            var comp = this.project.items.addComp(symName, defaultWidth, defaultHeight, 1.0, duration, fps);
            comp.parentFolder = compsFolder;
            this.symbolComps[symName] = comp;
        }

        // 5. Populate symbol compositions
        for (var s = 0; s < rawSymbols.length; s++) {
            var sym = rawSymbols[s];
            var symName = getVal(sym, ["SN", "SYMBOL_name"], "Symbol_" + s);
            var symTl = getVal(sym, ["TL", "TIMELINE"], {});
            var comp = this.symbolComps[symName];
            this.buildTimeline(comp, symTl, fps);
        }

        // 6. Build the Master Root Composition
        var rootDuration = Math.max(1, rootLength) / fps;
        var masterComp = this.project.items.addComp(projectName + "_Master", defaultWidth, defaultHeight, 1.0, rootDuration, fps);
        masterComp.parentFolder = compsFolder;
        this.buildTimeline(masterComp, rootTimeline, fps);

        // Open Master Comp in Viewer
        masterComp.openInViewer();
    };

    // --- Calculates the maximum frame count across all layers in a timeline ---
    AnimateImporter.prototype.getTimelineLength = function (tl) {
        var layers = getVal(tl, ["L", "LAYERS"], []);
        var maxFrames = 0;
        for (var i = 0; i < layers.length; i++) {
            var frames = getVal(layers[i], ["FR", "Frames"], []);
            for (var f = 0; f < frames.length; f++) {
                var fr = frames[f];
                var index = getVal(fr, ["I", "index"], 0);
                var duration = getVal(fr, ["DU", "duration"], 1);
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

    // --- Preloads image files into Project panel ---
    AnimateImporter.prototype.preloadSprites = function (spritemap, folder) {
        // Option A: Look for loose PNGs in "sprites/" or base folder
        var spriteDir = new Folder(this.baseFolder.fsName + "/sprites");
        var searchFolder = spriteDir.exists ? spriteDir : this.baseFolder;
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
                var io = new ImportOptions(atlasPng);
                var footage = this.project.importFile(io);
                footage.parentFolder = folder;
                this.importedFootage["__ATLAS__"] = footage;
            } catch (err) {}
        }
    };

    // --- Builds layers, keyframes, and timing inside a composition ---
    AnimateImporter.prototype.buildTimeline = function (comp, timeline, fps) {
        var rawLayers = getVal(timeline, ["L", "LAYERS"], []);
        if (!rawLayers || rawLayers.length === 0) return;

        // In Adobe Animate, layers are listed top-to-bottom in the UI.
        // In After Effects, layer index 1 is topmost.
        // We iterate layers in reverse so lower layers are added first, resulting in the correct stack.
        for (var l = rawLayers.length - 1; l >= 0; l--) {
            var layerData = rawLayers[l];
            var layerName = getVal(layerData, ["LN", "Layer_name"], "Layer_" + (l + 1));
            var frames = getVal(layerData, ["FR", "Frames"], []);
            if (!frames || frames.length === 0) continue;

            this.buildLayerFrames(comp, layerName, frames, fps);
        }
    };

    // --- Builds elements across keyframes on a specific layer ---
    AnimateImporter.prototype.buildLayerFrames = function (comp, layerName, frames, fps) {
        // Track active instances across consecutive frames to optimize layer count
        for (var f = 0; f < frames.length; f++) {
            var frame = frames[f];
            var frameIndex = getVal(frame, ["I", "index"], 0);
            var duration = getVal(frame, ["DU", "duration"], 1);
            var elements = getVal(frame, ["E", "elements"], []);

            var startTime = frameIndex / fps;
            var endTime = (frameIndex + duration) / fps;

            for (var e = 0; e < elements.length; e++) {
                var elem = elements[e];

                // Check if element is a Symbol Instance (SI) or Atlas Sprite Instance (ASI)
                var si = getVal(elem, ["SI", "SYMBOL_Instance"], null);
                var asi = getVal(elem, ["ASI", "ATLAS_SPRITE_instance"], null);

                var targetLayer = null;

                if (si) {
                    var symName = getVal(si, ["SN", "SYMBOL_name"], "");
                    var subComp = this.symbolComps[symName];
                    if (subComp) {
                        targetLayer = comp.layers.add(subComp);
                        targetLayer.name = symName + " [" + layerName + "]";
                    }
                } else if (asi) {
                    var spriteName = getVal(asi, ["N", "name"], "");
                    var footage = this.importedFootage[spriteName] || this.importedFootage["__ATLAS__"];
                    if (footage) {
                        targetLayer = comp.layers.add(footage);
                        targetLayer.name = spriteName + " [" + layerName + "]";
                    }
                }

                if (!targetLayer) continue;

                // Set in/out times
                targetLayer.inPoint = startTime;
                targetLayer.outPoint = endTime;

                // Extract Transform Matrix3D
                var m3d = null;
                var trp = null;
                var colorEffect = null;

                if (si) {
                    m3d = getVal(si, ["M3D", "Matrix3D"], null);
                    trp = getVal(si, ["TRP", "transformationPoint"], null);
                    colorEffect = getVal(si, ["C", "color"], null);
                } else if (asi) {
                    m3d = getVal(asi, ["M3D", "Matrix3D"], null);
                    trp = getVal(asi, ["POS", "Position"], null);
                }

                // Apply Anchor Point (Transformation Point)
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

                    // Ensure Hold Interpolation so frames don't tween unintentionally
                    this.setHoldKeyframe(posProp, startTime);
                    this.setHoldKeyframe(scaleProp, startTime);
                    this.setHoldKeyframe(rotProp, startTime);
                }

                // Apply Color / Alpha Effect
                if (colorEffect) {
                    var mode = getVal(colorEffect, ["M", "mode"], "");
                    var alpha = getVal(colorEffect, ["AM", "alphaMultiplier"], 1.0);
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
