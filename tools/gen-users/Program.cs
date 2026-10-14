// 批量生成“预置账号”的 SQL，不连接数据库。
// 用法：dotnet run -- <邮箱清单.txt> <默认密码> [输出.sql]
// 清单每行一个邮箱，可选“邮箱,昵称”；空行与 # 开头的行忽略。
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

if (args.Length < 2)
{
    Console.Error.WriteLine("用法：dotnet run -- <邮箱清单.txt> <默认密码> [输出.sql]");
    return 1;
}

var listPath = args[0];
var password = args[1];
var outPath = args.Length > 2 ? args[2] : "seed-users.sql";

if (password.Length < 8 || password.Length > 128)
{
    Console.Error.WriteLine("默认密码长度必须在 8 到 128 之间（与注册规则一致）。");
    return 1;
}

static string Sql(string value) => "'" + value.Replace("'", "''") + "'";

var hasher = new PasswordHasher<object>();
var seen = new HashSet<string>();
var sql = new StringBuilder();
sql.AppendLine("-- 预置账号：已存在的邮箱会被跳过（ON CONFLICT DO NOTHING）。");
sql.AppendLine("BEGIN;");

var count = 0;
foreach (var raw in File.ReadAllLines(listPath, Encoding.UTF8))
{
    var line = raw.Trim().TrimStart('﻿');
    if (line.Length == 0 || line.StartsWith('#')) continue;

    var parts = line.Split(new[] { ',', '，' }, 2);
    var email = parts[0].Trim();
    var nickname = parts.Length > 1 ? parts[1].Trim() : "";

    var at = email.IndexOf('@');
    if (email.Length > 254 || at <= 0 || at == email.Length - 1 || email.Any(char.IsWhiteSpace))
    {
        Console.Error.WriteLine($"跳过无效邮箱：{email}");
        continue;
    }
    // 与 AuthService.NormalizeEmail 保持一致：Trim + ToUpperInvariant
    var normalized = email.ToUpperInvariant();
    if (!seen.Add(normalized))
    {
        Console.Error.WriteLine($"跳过重复邮箱：{email}");
        continue;
    }
    if (nickname.Length == 0) nickname = $"玩家{RandomNumberGenerator.GetInt32(100000, 1000000)}";
    if (nickname.Length > 32) nickname = nickname[..32];

    var hash = hasher.HashPassword(new object(), password);
    sql.AppendLine(
        "INSERT INTO users (id, nickname, email, normalized_email, email_verified_at, password_hash, status, created_at, updated_at) " +
        $"VALUES (gen_random_uuid(), {Sql(nickname)}, {Sql(email)}, {Sql(normalized)}, now(), {Sql(hash)}, 'active', now(), now()) " +
        "ON CONFLICT (normalized_email) WHERE normalized_email IS NOT NULL DO NOTHING;");
    count++;
}

sql.AppendLine("COMMIT;");
File.WriteAllText(outPath, sql.ToString(), new UTF8Encoding(false));
Console.WriteLine($"已生成 {count} 个账号 -> {outPath}");
return 0;
