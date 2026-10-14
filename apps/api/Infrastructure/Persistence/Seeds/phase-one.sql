-- 一期正式基础数据；手动执行，不在 API 启动时自动写入。
-- 流派名称参考：https://news.17173.com/content/06132026/140416745.shtml
-- 副本配置由用户确认：锋锷断迷狂，承伤 1 / 奶妈 2 / 输出 7，30 分钟。
-- 重复执行只新增缺失项，不覆盖已有字典或系统模板配置。
BEGIN;

SELECT pg_advisory_xact_lock(72420261013);

INSERT INTO public.profile_style_options (code, label, sort_order, enabled, updated_at)
SELECT seed.code, seed.label, seed.sort_order, TRUE, CURRENT_TIMESTAMP
FROM (VALUES
    ('mingjin_hong', '鸣金虹', 10),
    ('mingjin_ying', '鸣金影', 20),
    ('qiansi_yu', '牵丝玉', 30),
    ('qiansi_lin', '牵丝霖', 40),
    ('qiansi_yi', '牵丝翊', 50),
    ('lieshi_wei', '裂石威', 60),
    ('lieshi_jun', '裂石钧', 70),
    ('pozhu_feng', '破竹风', 80),
    ('pozhu_chen', '破竹尘', 90),
    ('pozhu_yuan', '破竹鸢', 100)
) AS seed(code, label, sort_order)
WHERE NOT EXISTS (
    SELECT 1 FROM public.profile_style_options existing WHERE existing.label = seed.label
)
ON CONFLICT (code) DO NOTHING;

INSERT INTO public.dungeon_templates (
    id, guild_id, created_by, name, need_tank, need_healer, need_dps,
    default_duration_min, archived_at, version, created_at, updated_at
)
SELECT
    '72420261-1013-4000-8000-000000000001'::uuid,
    NULL, NULL, '锋锷断迷狂', 1, 2, 7, 30,
    NULL, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
WHERE NOT EXISTS (
    SELECT 1 FROM public.dungeon_templates
    WHERE guild_id IS NULL AND name = '锋锷断迷狂'
)
ON CONFLICT (id) DO NOTHING;

COMMIT;
