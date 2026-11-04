import shutil
import subprocess
from pathlib import Path

import pytest

from aether.decompile import (
    LIBRARY_RESOURCES,
    PATCHABLE_LIBRARIES,
    expand_leading_tabs,
    library_source_path,
    normalize_line_endings,
)


class TestNormalizeLineEndings:
    def test_crlf_becomes_lf(self):
        assert normalize_line_endings(b"a\r\nb\r\n") == b"a\nb\n"

    def test_cr_before_end_of_file_is_stripped(self):
        assert normalize_line_endings(b"a\r\nb\r") == b"a\nb"

    def test_lone_cr_inside_a_line_is_kept(self):
        assert normalize_line_endings(b"a\rb\n") == b"a\rb\n"

    def test_lf_input_is_unchanged(self):
        data = '{\n  "k": "日本語"\n}\n'.encode()
        assert normalize_line_endings(data) == data


EXPAND_SAMPLES = [
    b"\tint x;\n",
    b"\t\tif (a)\n\t\t{\n\t\t}\n",
    b"  \tmixed;\n",  # spaces then a tab: the tab advances to the next 4-column stop
    b'\tx = "a\\tb";\tcomment\n',  # tabs after the first non-blank are kept
    b"no indent\n",
    b"\t",
    b"",
]


@pytest.mark.parametrize("data", EXPAND_SAMPLES)
def test_expand_leading_tabs_matches_coreutils_expand(data):
    if shutil.which("expand") is None:
        pytest.skip("coreutils expand not available")
    expected = subprocess.run(
        ["expand", "-i", "-t", "4"], input=data, capture_output=True, check=True
    ).stdout
    assert expand_leading_tabs(data) == expected


def test_expand_leading_tabs_examples():
    assert expand_leading_tabs(b"\t\tfoo();\n") == b"        foo();\n"
    assert expand_leading_tabs(b"  \tbar\tbaz\n") == b"    bar\tbaz\n"
    assert expand_leading_tabs(b"x\ty\n") == b"x\ty\n"


def test_library_resources_map_to_unique_dll_names():
    names = list(LIBRARY_RESOURCES.values())
    assert len(set(names)) == len(names)
    assert all(name.endswith(".dll") for name in names)
    assert all(
        res.startswith("Terraria.Libraries.") and res.endswith(name)
        for res, name in LIBRARY_RESOURCES.items()
    )


@pytest.mark.parametrize(
    ("rel", "expected"),
    [
        ("ReLogic/Content/Asset.cs", "Content/Asset.cs"),
        ("ReLogic/Utilities/SlotId.cs", "Utilities/SlotId.cs"),
        ("Properties/AssemblyInfo.cs", "Properties/AssemblyInfo.cs"),
        ("ReLogic.cs", "ReLogic.cs"),  # a file, not the namespace folder
    ],
)
def test_library_source_path_drops_the_root_namespace_folder(rel, expected):
    assert library_source_path(Path(rel), "ReLogic") == Path(expected)


def test_patchable_libraries_are_extracted_libraries():
    assert set(PATCHABLE_LIBRARIES) <= set(LIBRARY_RESOURCES.values())
