"""Regression: app branding uses the approved A1 x A3 icon set on Windows and macOS.

No external ZIP is required: exact checksums identify the approved canonical art.
The baseline test deliberately fails on the previous colorful branding.
"""
import hashlib
import json
import struct
import unittest
from pathlib import Path
from xml.etree import ElementTree

REPO = Path(__file__).resolve().parents[3]
WINDOWS_ASSETS = "windows/MarkdownStudioPro.WinUI/Assets/"
MAC_ASSETS = "macos/MarkdownStudioProMac/Assets.xcassets/AppIcon.appiconset/"

EXPECTED = {
    WINDOWS_ASSETS + "markdown_studio_icon.ico": "033bd570dc806fa0d0d15f143808ed09c8981cba9504585cfdef5a2edf312295",
    WINDOWS_ASSETS + "markdown_studio_icon.png": "6f98535bbd84aa42158a12d207c21aa28c5a61c32070131052d45e30c5656f88",
    MAC_ASSETS + "appicon_16.png": "6f3aa01aba376da1d46e9160afa4e11f030fbb43a8d833da210816f2102f536d",
    MAC_ASSETS + "appicon_32.png": "f387296cad9b36f9659a74ab94d2e4a5512e0e9a2099099d19901c8f2632c221",
    MAC_ASSETS + "appicon_64.png": "5f9a11c6b900bdbb07977e09f9dd83b2b2348817bc31d88be6bfb10227c5d451",
    MAC_ASSETS + "appicon_128.png": "9b6708b33a618dfbb07b6cff3d5917e8aaa81d1e79b70bc4961fccb96db5f1d1",
    MAC_ASSETS + "appicon_256.png": "29add9e5dd7e4f3cd9d8de78f55dc658cf151458c1448821e475e57ef4030b51",
    MAC_ASSETS + "appicon_512.png": "4a341e250c7cd58247ead5ff0c570b913af89ce5b70cb902e8dcbbea9503df69",
    MAC_ASSETS + "appicon_1024.png": "6f98535bbd84aa42158a12d207c21aa28c5a61c32070131052d45e30c5656f88",
    "branding/msp-light.svg": "80fe4e77bcf1d9fabdbe6e16701e425a76faaa5dee1830aa48f9a3de2a2a2e1b",
    "branding/msp-dark.svg": "cc0c134b59fbfb761e5e3890f49598f6d6c0b4c7099bcd1b005082ede439c220",
}

class BrandingAssetsRegression(unittest.TestCase):
    def test_canonical_approved_assets_replaced_existing_logos(self):
        for relative, expected in EXPECTED.items():
            with self.subTest(asset=relative):
                data = (REPO / relative).read_bytes()
                self.assertEqual(hashlib.sha256(data).hexdigest(), expected)

    def test_png_dimensions_and_readable_ico(self):
        for relative in EXPECTED:
            if not relative.endswith(".png"):
                continue
            with self.subTest(asset=relative):
                data = (REPO / relative).read_bytes()
                self.assertEqual(data[:8], b"\x89PNG\r\n\x1a\n")
                width, height = struct.unpack(">II", data[16:24])
                expected_size = 1024 if relative.startswith(WINDOWS_ASSETS) else int(Path(relative).stem.split("_")[-1])
                self.assertEqual((width, height), (expected_size, expected_size))
        data = (REPO / WINDOWS_ASSETS / "markdown_studio_icon.ico").read_bytes()
        reserved, kind, count = struct.unpack_from("<HHH", data)
        self.assertEqual((reserved, kind), (0, 1))
        self.assertGreaterEqual(count, 4)
        sizes = {entry[0] or 256 for entry in (
            struct.unpack_from("<BBBBHHII", data, 6 + i * 16) for i in range(count)
        )}
        self.assertTrue({16, 32, 256}.issubset(sizes), sizes)

    def test_platforms_reference_same_branding_and_have_no_extra_legacy_app_icons(self):
        project = (REPO / "windows/MarkdownStudioPro.WinUI/MarkdownStudioPro.WinUI.csproj").read_text(encoding="utf-8")
        window = (REPO / "windows/MarkdownStudioPro.WinUI/MainWindow.xaml.cs").read_text(encoding="utf-8")
        readme = (REPO / "README.md").read_text(encoding="utf-8")
        self.assertIn(r"<ApplicationIcon>Assets\markdown_studio_icon.ico</ApplicationIcon>", project)
        self.assertIn('appWindow.SetIcon(iconPath)', window)
        self.assertIn('markdown_studio_icon.png', readme)
        self.assertEqual({p.name for p in (REPO / WINDOWS_ASSETS).iterdir()}, {"markdown_studio_icon.ico", "markdown_studio_icon.png"})

        macdir = REPO / MAC_ASSETS
        manifest = json.loads((macdir / "Contents.json").read_text(encoding="utf-8"))
        expected_mac = {"appicon_" + str(size) + ".png" for size in (16,32,64,128,256,512,1024)}
        self.assertEqual({i["filename"] for i in manifest["images"]}, expected_mac)
        self.assertEqual({p.name for p in macdir.iterdir()}, expected_mac | {"Contents.json"})

    def test_svg_sources_are_local_and_safe(self):
        for name in ("msp-light.svg", "msp-dark.svg"):
            data = (REPO / "branding" / name).read_bytes()
            root = ElementTree.fromstring(data)
            self.assertTrue(root.tag.endswith("svg"))
            self.assertNotIn(b"<script", data.lower())
            for element in root.iter():
                self.assertFalse(element.tag.endswith("script"), element.tag)
                for key, value in element.attrib.items():
                    if key.endswith("href") or key.endswith("src"):
                        self.assertFalse(value.startswith(("http://", "https://", "//")), (name, key, value))

if __name__ == "__main__":
    unittest.main(verbosity=2)
