#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把语义画刷键的 {StaticResource X} 批量替换为 {DynamicResource X}。

白名单驱动：只替换语义层（随主题变化）的键，Metrics / Palette / Style
引用一律保持 StaticResource，避免破坏模板与 BasedOn 解析。
"""

import re
import sys
from pathlib import Path

SEMANTIC_KEYS = [
    "SurfaceBase", "SurfaceSunken", "SurfaceRaised", "SurfaceMuted",
    "SurfaceOverlay", "SurfaceAccentSubtle",
    "BorderSubtle", "BorderDefault", "BorderStrong", "BorderAccent",
    "TextPrimary", "TextSecondary", "TextTertiary", "TextPlaceholder",
    "TextDisabled", "TextAccent", "TextOnAccent", "TextDanger",
    "AccentDefault", "AccentHover", "AccentPressed", "AccentDisabled",
    "StateHover", "StatePressed", "StateSelected", "StateSelectedHover",
    "FocusRing",
    "FeedbackSuccess", "FeedbackSuccessBg", "FeedbackWarning",
    "FeedbackWarningBg", "FeedbackDanger", "FeedbackDangerBg",
    "TransparentBrush",
]

# 注意：FocusRing 必须精确匹配，不能误伤 FocusRingStyle（Style 引用需保持静态）
PATTERN = re.compile(
    r"\{StaticResource\s+(" + "|".join(sorted(SEMANTIC_KEYS, key=len, reverse=True)) + r")\s*\}"
)

TARGETS = [
    "src/Epubra.App/Styles/Controls.xaml",
    "src/Epubra.App/Views/MainWindow.xaml",
    "src/Epubra.App/Views/MetadataDialog.xaml",
]


def main() -> int:
    root = Path(__file__).resolve().parent.parent
    total = 0
    for rel in TARGETS:
        path = root / rel
        text = path.read_text(encoding="utf-8")
        new_text, count = PATTERN.subn(r"{DynamicResource \1}", text)
        if count:
            path.write_text(new_text, encoding="utf-8")
        total += count
        remaining = len(re.findall(r"\{StaticResource\s", new_text))
        print(f"{rel}: replaced={count}, remaining StaticResource={remaining}")
    print(f"TOTAL replaced = {total}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
