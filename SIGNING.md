# 代码签名 / Code signing

## 现状:不签名(2026-09-27 起)

**SignPath Foundation 的免费签名申请未通过。** 所以官方构建**不带数字签名**,
发布说明里也不再提"等待签名审批"这类话。

对用户的影响和处理办法:

- 首次运行会弹「Windows 已保护你的电脑」(SmartScreen) —— 这是未签名软件的**预期行为**,
  不代表文件有害:点「更多信息」→「仍要运行」即可;或者右键 exe →「属性」→ 勾「解除锁定」。
- 想先核对再运行:对比 Release 说明里给出的 SHA256。

**Official builds are unsigned.** The free signing application to SignPath
Foundation was declined, so releases carry no Authenticode signature. SmartScreen
will warn on first run; that is expected for unsigned software. Verify the file
against the SHA256 in the release notes before running if you prefer.

## CI 现在的行为

- 仓库里没配 `SIGNPATH_*` 变量 → `SIGNING_ENABLED=false` → tag 构建走**未签名**路径:
  照常出包、照常发 Release,这不是失败。
- 工作流里那套 SignPath 步骤**保留着** —— 将来接上任何可用的签名服务,配好变量就能启用。

## 将来要签名的话

- 可选:SignPath 付费项目 / Azure Trusted Signing / 其他提供 Authenticode + 时间戳的服务。
- 需要:代码签名证书(EV 或 OV)、CI 变量与密钥、以及"内部文件与最终 exe 各签一次"的顺序。
- 唯一不能破的规矩:**签名是打包的最后一环**,签完不要再改动任何字节,否则签名失效。

## 附:原 SignPath 配置说明(仅供将来参考)

1. Apply at <https://signpath.org/apply>.
2. Create a SignPath project for this repository.
3. Link the project to the predefined **GitHub.com** trusted build system.
4. Create an artifact configuration named `installer-inner` using
   [`.signpath/installer-inner-artifact-configuration.xml`](.signpath/installer-inner-artifact-configuration.xml).
5. Create an artifact configuration named `installer-setup` using
   [`.signpath/installer-setup-artifact-configuration.xml`](.signpath/installer-setup-artifact-configuration.xml).
6. Create a release signing policy, for example `release-signing`.
7. Create an API token for a user that can submit requests to this project.

Repository variables: `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
`SIGNPATH_SIGNING_POLICY_SLUG`, `SIGNPATH_INNER_ARTIFACT_CONFIGURATION_SLUG`,
`SIGNPATH_FINAL_ARTIFACT_CONFIGURATION_SLUG`; secret: `SIGNPATH_API_TOKEN`.
