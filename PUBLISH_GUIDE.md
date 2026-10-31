# 发布操作手册（你照着做，或者登录后叫我执行）

> 目录：`E:\deepseek\release\dsh-whale-desktop`
> 包名：`dsh-whale-desktop`　显示名：**小鲸鱼桌宠**

---

## 第 0 步：注册 GitHub（你来做）

1. 打开 https://github.com/signup
2. 填邮箱、密码、用户名（**用户名请记下来，后面替换 qingfenganshi**）
3. 验证邮箱
4. 告诉我你的**用户名**，我帮你把所有占位符替换掉

---

## 第 1 步：建仓库（你来做，1 分钟）

1. 打开 https://github.com/new
2. Repository name 填：`dsh-whale-desktop`
3. 选 **Public**
4. **不要**勾选 "Add a README file"（我们已有）
5. 点 **Create repository**

---

## 第 2 步：上传代码（二选一）

### 方式 A：网页上传（最简单，不用装 Git）

1. 在刚建好的仓库页面点 **uploading an existing file**
2. 把 `E:\deepseek\release\dsh-whale-desktop` 里的**所有文件**拖进去
   - `.npmignore`、`.gitignore` 这两个文件是隐藏文件，需要先显示隐藏文件再拖
3. 点 **Commit changes**

### 方式 B：用 Git（如果你装了）

```bash
cd E:\deepseek\release\dsh-whale-desktop
git init
git add -A
git commit -m "feat: 小鲸鱼桌宠 v0.1.0 —— DSH 鲸鱼挂件衍生版"
git branch -M main
git remote add origin https://github.com/<你的用户名>/dsh-whale-desktop.git
git push -u origin main
```

---

## 第 3 步：添加 topic（网页操作）

1. 仓库首页 → 右侧 About 区域 → 齿轮图标 ⚙️
2. **Topics** 里依次添加：
   ```
   dsh-plugin
   deepseek
   deepseek-harness
   desktop-pet
   widget
   ```
3. 顺手把 **Description** 填上：
   ```
   小鲸鱼桌宠：DeepSeek Harness 余额挂件衍生版，支持对话气泡与独立桌面小鲸鱼
   ```
4. 保存

---

## 第 4 步：发布到 npm（你来做，需要账号）

### 4.1 注册 npm

1. 打开 https://www.npmjs.com/signup
2. 注册并**验证邮箱**（必须验证，否则发布会被拒）
3. 告诉我你的 npm 用户名

### 4.2 在本地登录并发布

```bash
# 登录（会打开浏览器）
npm login

# 确认账号
npm whoami

# 进入发布目录
cd E:\deepseek\release\dsh-whale-desktop

# 先干跑一遍，确认要发的文件
npm pack --dry-run

# 正式发布
npm publish
```

> 如果你的 npm 账号开了 2FA，`npm publish` 会要求输入验证码。

### 4.3 验证

```bash
npm view dsh-whale-desktop
```

浏览器验证：https://www.npmjs.com/package/dsh-whale-desktop

---

## 第 5 步：提交到聚合目录（发 PR）

### 5.1 先确认目标仓库是否存在

⚠️ **我没有联网核实过** `Oh-My-DSH` / `awesome-dsh-plugin` 这两个仓库是否真实存在。
你需要先搜索确认：

```
GitHub 搜索：awesome dsh plugin
GitHub 搜索：dsh-plugin list
```

找到后按对方 README 的贡献说明提 PR：

```bash
# 1. Fork 对方仓库（网页点 Fork）
git clone https://github.com/<你的用户名>/<对方仓库>.git
cd <对方仓库>

# 2. 在其 README 的插件列表里加一行
- [小鲸鱼桌宠](https://github.com/<你的用户名>/dsh-whale-desktop) —— DSH 余额挂件衍生版，支持对话与桌面桌宠

# 3. 提交
git checkout -b add-dsh-whale-desktop
git add README.md
git commit -m "Add dsh-whale-desktop"
git push origin add-dsh-whale-desktop
```

然后在 GitHub 网页上点 **Compare & pull request**。

### 5.2 如果找不到聚合目录

替代宣传渠道：
- DSH 官方社区 / 讨论区发帖
- 在 npm 包的 README 里做好 SEO（关键词已配置）
- 在 B 站 / 小红书发演示视频（你已录好）

---

## 说明视频

你说已录好视频 —— 建议放在**第 5 步之后**发：

1. 上传到 B 站 / 小红书
2. 视频简介里放 GitHub 仓库链接
3. 在 GitHub 仓库的 About 里填视频链接

**上传需要你自己登录账号操作**，我这边无法代传。

---

## 授权给我的方式（如果不想自己敲命令）

如果你愿意让我**直接执行**第 4~5 步，需要：

| 操作 | 授权方式 |
|---|---|
| npm 发布 | 你在本地终端执行 `npm login` 后，我就能用你的登录态执行 `npm publish` |
| GitHub 推送 | 你安装 [GitHub CLI](https://cli.github.com/) 并执行 `gh auth login` 后，我就能建仓库、推送、提 PR |

**这两种方式都只需要你登录一次，我不会接触你的密码。**

---

## 检查清单（发布前逐项确认）

- [ ] GitHub 用户名已告诉我，`qingfenganshi` 已全部替换
- [ ] LICENSE 里保留了 `Copyright (c) 2026 MeteorNOX`
- [ ] THIRD_PARTY_NOTICES.md 已包含
- [ ] README 写明「基于 MeteorNOX 修改，原许可证为 MIT」
- [ ] package.json 有 `license` / `repository` / `dsh.bundle` / `engines`
- [ ] `npm pack --dry-run` 里**没有** `apikey.txt`
- [ ] 本地插件里点 🔑 → 清空，**确认过没泄露 key**
- [ ] 仓库已建、已选 Public
- [ ] topic 已加 `dsh-plugin`
- [ ] npm 邮箱已验证
