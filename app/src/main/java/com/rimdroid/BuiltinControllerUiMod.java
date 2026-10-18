package com.rimdroid;

import android.content.Context;
import android.content.res.AssetManager;
import android.util.Log;

import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.Node;
import org.w3c.dom.NodeList;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.file.AtomicMoveNotSupportedException;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;

import javax.xml.parsers.DocumentBuilderFactory;
import javax.xml.transform.OutputKeys;
import javax.xml.transform.Transformer;
import javax.xml.transform.TransformerFactory;
import javax.xml.transform.dom.DOMSource;
import javax.xml.transform.stream.StreamResult;

/** Installs and activates RimDroid's own controller-UI bridge mod.
 *
 * The bundled files are entirely RimDroid-authored. The mod runs on RimWorld's managed loading
 * thread and merely enables an existing controller-oriented UI when the launcher exports
 * RIMDROID_CONTROLLER_UI=1. No game assembly is bundled or modified, and Harmony is not required.
 */
public final class BuiltinControllerUiMod {
    private static final String TAG = "RimDroid/ControllerUI";
    private static final String ASSET_ROOT = "builtinmods/RimDroidControllerUI";
    private static final String MOD_DIR = "RimDroidControllerUI";
    private static final String PACKAGE_ID = "rimdroid.controllerui";

    /** Idempotent and safe to call from install, startup reconcile, and immediately before launch. */
    public static synchronized void install(Context context, File instanceDir) {
        if (context == null || instanceDir == null || !instanceDir.isDirectory()) return;
        // Not for RimWorld 1.7 (its own engine on .NET): the mod carries a .NET Framework build of
        // Lib.Harmony for 1.6, which there only collides with the Harmony mod's 1.7 build.
        if (new File(instanceDir, "RimWorld.runtimeconfig.json").isFile()) {
            // Instances made before this check got it anyway: take it out (folder and load order).
            File stale = new File(instanceDir, "Mods/" + MOD_DIR);
            if (stale.isDirectory()) {
                setActive(instanceDir, false);
                deleteTree(stale);
                Log.i(TAG, "Built-in controller mod removed from RimWorld 1.7 instance " + instanceDir.getName());
            }
            return;
        }
        try {
            File modDir = new File(instanceDir, "Mods/" + MOD_DIR);
            copyAssetTree(context.getAssets(), ASSET_ROOT, modDir);
        } catch (Throwable t) {
            // Controller support must never make an otherwise valid game instance unlaunchable.
            Log.w(TAG, "Built-in controller mod install skipped for " + instanceDir.getName(), t);
        }
    }

    /** Match ModsConfig to this launch. The DLL stays installed, but keyboard/touch-only saves do
     *  not carry an unnecessary RimDroid mod entry in their active mod list. */
    public static synchronized void setActive(File instanceDir, boolean enabled) {
        if (instanceDir == null || !instanceDir.isDirectory()) return;
        try {
            if (enabled) {
                File modDir = new File(instanceDir, "Mods/" + MOD_DIR);
                if (!new File(modDir, "About/About.xml").isFile()
                        || !new File(modDir, "Assemblies/RimDroid.ControllerUI.dll").isFile()) {
                    Log.w(TAG, "Not activating incomplete built-in controller mod in "
                            + instanceDir.getName());
                    return;
                }
            }
            updateActiveState(instanceDir, enabled);
        } catch (Throwable t) {
            // Controller support must never make an otherwise valid game instance unlaunchable.
            Log.w(TAG, "Built-in controller mod activation skipped for " + instanceDir.getName(), t);
        }
    }

    private static void deleteTree(File f) {
        File[] kids = f.listFiles();
        if (kids != null) for (File k : kids) deleteTree(k);
        //noinspection ResultOfMethodCallIgnored
        f.delete();
    }

    private static void copyAssetTree(AssetManager assets, String assetPath, File output) throws Exception {
        String[] children = assets.list(assetPath);
        if (children != null && children.length > 0) {
            if (!output.isDirectory() && !output.mkdirs())
                throw new java.io.IOException("Cannot create " + output);
            for (String child : children)
                copyAssetTree(assets, assetPath + "/" + child, new File(output, child));
            return;
        }

        File parent = output.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs())
            throw new java.io.IOException("Cannot create " + parent);
        File temp = new File(output.getPath() + ".rdtmp");
        try (java.io.InputStream in = assets.open(assetPath);
             FileOutputStream out = new FileOutputStream(temp)) {
            byte[] buffer = new byte[16 * 1024];
            int count;
            while ((count = in.read(buffer)) != -1) out.write(buffer, 0, count);
        }
        replaceFile(temp, output);
    }

    private static void updateActiveState(File instanceDir, boolean enabled) throws Exception {
        File config = new File(instanceDir,
                "unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Config/ModsConfig.xml");
        Document document;
        Element activeMods;

        if (config.isFile()) {
            document = newDocumentBuilderFactory().newDocumentBuilder().parse(config);
            NodeList active = document.getElementsByTagName("activeMods");
            if (active.getLength() == 0)
                throw new java.io.IOException("ModsConfig.xml has no <activeMods>");
            activeMods = (Element) active.item(0);
            boolean present = containsPackage(activeMods, PACKAGE_ID);
            if (present == enabled) return;
            if (!enabled) removePackage(activeMods, PACKAGE_ID);
        } else {
            if (!enabled) return;
            File parent = config.getParentFile();
            if (parent != null && !parent.isDirectory() && !parent.mkdirs())
                throw new java.io.IOException("Cannot create " + parent);
            document = com.rimdroid.mods.ModList.newConfig(instanceDir);
            activeMods = (Element) document.getElementsByTagName("activeMods").item(0);
        }

        if (enabled) appendText(document, activeMods, "li", PACKAGE_ID);
        writeDocumentAtomically(document, config);
        Log.i(TAG, (enabled ? "Activated " : "Deactivated ") + PACKAGE_ID
                + " in " + instanceDir.getName());
    }

    private static DocumentBuilderFactory newDocumentBuilderFactory() {
        DocumentBuilderFactory factory = DocumentBuilderFactory.newInstance();
        factory.setExpandEntityReferences(false);
        try { factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true); }
        catch (Throwable ignored) {}
        try { factory.setFeature("http://xml.org/sax/features/external-general-entities", false); }
        catch (Throwable ignored) {}
        try { factory.setFeature("http://xml.org/sax/features/external-parameter-entities", false); }
        catch (Throwable ignored) {}
        return factory;
    }

    private static boolean containsPackage(Element activeMods, String packageId) {
        NodeList children = activeMods.getChildNodes();
        for (int i = 0; i < children.getLength(); i++) {
            Node child = children.item(i);
            if (child.getNodeType() == Node.ELEMENT_NODE && "li".equals(child.getNodeName())
                    && packageId.equalsIgnoreCase(child.getTextContent().trim()))
                return true;
        }
        return false;
    }

    private static void removePackage(Element activeMods, String packageId) {
        NodeList children = activeMods.getChildNodes();
        for (int i = children.getLength() - 1; i >= 0; i--) {
            Node child = children.item(i);
            if (child.getNodeType() == Node.ELEMENT_NODE && "li".equals(child.getNodeName())
                    && packageId.equalsIgnoreCase(child.getTextContent().trim()))
                activeMods.removeChild(child);
        }
    }

    private static void appendText(Document document, Element parent, String name, String value) {
        Element child = document.createElement(name);
        child.appendChild(document.createTextNode(value));
        parent.appendChild(child);
    }

    private static void writeDocumentAtomically(Document document, File output) throws Exception {
        Transformer transformer = TransformerFactory.newInstance().newTransformer();
        transformer.setOutputProperty(OutputKeys.ENCODING, "utf-8");
        transformer.setOutputProperty(OutputKeys.INDENT, "yes");
        transformer.setOutputProperty("{http://xml.apache.org/xslt}indent-amount", "2");

        File temp = new File(output.getPath() + ".rdtmp");
        try (FileOutputStream stream = new FileOutputStream(temp)) {
            transformer.transform(new DOMSource(document), new StreamResult(stream));
        }
        replaceFile(temp, output);
    }

    private static void replaceFile(File source, File destination) throws Exception {
        try {
            Files.move(source.toPath(), destination.toPath(), StandardCopyOption.REPLACE_EXISTING,
                    StandardCopyOption.ATOMIC_MOVE);
        } catch (AtomicMoveNotSupportedException ignored) {
            Files.move(source.toPath(), destination.toPath(), StandardCopyOption.REPLACE_EXISTING);
        }
    }

    private BuiltinControllerUiMod() {}
}
