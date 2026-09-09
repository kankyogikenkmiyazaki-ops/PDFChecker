# AGENTS.md
## AI Desk 開発エージェント入口

このRepositoryで作業するAIは、実装を始める前に以下の順で現在状態を確認する。

---

# 1. 最初に読む場所

`docs/` 直下にある現行 `.md` ファイルを確認する。

特に以下を優先する。

```text
AI_APP_DEVELOPMENT_RULES_*.md
AI_HANDOFF.md
現在Versionの仕様書
```

通常作業では、以下は確認しない。

```text
docs/archive/
```

`docs/archive/` は過去Versionの保管場所であり、ユーザーから明示的な指示がない限り読み込まない。

開発ルール本文にクイックリファレンスがある場合は、まずそこを確認し、詳細Sectionは今回の作業に必要な場合に参照する。

---

# 2. 情報の優先順位

現在状態を判断するときは、原則として以下を優先する。

1. 現在の実Project
2. `docs/` 直下の現行Markdown
3. Gitの実状態
4. 現在のユーザー指示

実Project・docs・Git等が食い違い、現在状態を一意に判断できない場合は推測で補完せず、人間へ確認する。

---

# 3. 作業開始前のGit確認

コード変更前に最低限以下を確認する。

```text
git status
git branch --show-current
git log -1 --oneline --decorate
```

必要に応じて、

```text
dotnet build
```

も確認する。

現在のGit状態を把握せずに変更を開始しない。

---

# 4. AI_HANDOFFとcommitの基本フロー

通常は以下の順で進める。

```text
現行docs確認
↓
Git状態確認
↓
実装
↓
build・必要な確認
↓
AI_HANDOFFの関係Sectionを更新
↓
git diff
↓
commit
```

`AI_HANDOFF.md` は原則としてcommit直前に更新し、コード変更と同じcommitへ含める。

詳細なHANDOFF更新ルールは、開発ルール本文を正とする。

---

# 5. commit / pushの権限

AIはcommitまで行ってよい。

```text
git push
```

は、ユーザーから明示的な指示がない限り実行しない。

Project全体の丸ごとバックアップも、ユーザーから明示的な指示がない限りAIは作成しない。

---

# 6. 基本原則

- 既存UI・操作方法・見える既存動作を勝手に変更しない。
- 依頼外の改善を勝手に実装しない。
- 内部実装は、既存仕様を守れる範囲でAIが自律的に進める。
- 将来仕様に影響する問題は、勝手に変更せず提案する。
- AI使用量を開発コストとして意識する。
- 新しい開発用Markdownを必要以上に増やさない。
- 詳細な判断は `docs/` 直下のAI開発ルール本文を正とする。

> **AGENTS.mdは入口であり、詳細ルールの正本ではない。**
