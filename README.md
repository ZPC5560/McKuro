# donet

基于 .NET 10 (C#) 的项目。

## 环境要求

- .NET SDK 10.0 (`dotnet --version` 应显示 10.x)

## 项目结构

```
donet/
├── donet.sln              # 解决方案
├── src/donet/             # 主程序 (console)
└── tests/donet.Tests/     # xUnit 单元测试
```

## 常用命令

```bash
dotnet restore   # 还原依赖
dotnet build     # 构建
dotnet run --project src/donet   # 运行
dotnet test      # 运行测试
```
