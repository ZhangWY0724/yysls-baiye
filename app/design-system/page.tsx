"use client";

import { useState } from "react";
import {
  Bell, CalendarDays, Check, ChevronRight, Clock3, Copy, HeartPulse,
  LockKeyhole, Search, Settings2, Shield, Sparkles, Star, Swords,
  TriangleAlert, UserRound, X,
} from "lucide-react";

const roles = [
  { label: "承伤", icon: Shield, tone: "role-tank" },
  { label: "奶妈", icon: HeartPulse, tone: "role-healer" },
  { label: "输出", icon: Swords, tone: "role-dps" },
];

const participants = [
  { name: "玄山", power: 7210, schools: "裂石钧", position: "承伤" },
  { name: "青禾", power: 6650, schools: "牵丝霖", position: "奶妈" },
  { name: "鹿鸣", power: 6890, schools: "牵丝霖、牵丝霜", position: "奶妈" },
  { name: "云归", power: 7042, schools: "鸣金虹", position: "输出" },
  { name: "砚秋", power: 6920, schools: "鸣金影", position: "输出" },
  { name: "昭月", power: 7156, schools: "破竹风", position: "输出" },
  { name: "舟遥", power: 6730, schools: "牵丝霜", position: "输出" },
  { name: "听雨", power: 6988, schools: "鸣金虹、鸣金影", position: "输出" },
];
const weekDays = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
const weekEvents = [
  { id: 1, day: 0, date: "10月12日", time: "20:00", name: "无名剑 · 试炼场" },
  { id: 2, day: 2, date: "10月14日", time: "20:30", name: "无名剑 · 试炼场" },
  { id: 3, day: 4, date: "10月16日", time: "21:00", name: "无名剑 · 试炼场" },
  { id: 4, day: 5, date: "10月17日", time: "19:30", name: "无名剑 · 试炼场" },
  { id: 5, day: 5, date: "10月17日", time: "21:30", name: "无名剑 · 试炼场" },
  { id: 6, day: 6, date: "10月18日", time: "20:00", name: "无名剑 · 试炼场" },
];

export default function DesignSystemPage() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [selectedEvent, setSelectedEvent] = useState(weekEvents[0]);
  const [saved, setSaved] = useState(false);

  return (
    <main className="ds-page">
      <header className="topbar ds-width">
        <div className="brand"><span className="brand-mark">燕</span><span>百业组队</span></div>
        <div className="topbar-actions"><button className="icon-button" aria-label="通知"><Bell size={19} /></button><button className="avatar" aria-label="个人资料">云</button></div>
      </header>

      <section className="hero ds-width">
        <div className="eyebrow"><Sparkles size={15} /> 视觉标准 · v0.1</div>
        <h1>让排班信息，<em>有秩序地流动。</em></h1>
        <p>这是一页可交互的前端基准。颜色、间距、圆角、状态和动效都从这里开始，后续页面只复用规则。</p>
        <div className="hero-actions"><button className="button button-primary" onClick={() => setDrawerOpen(true)}>打开场次抽屉 <ChevronRight size={17} /></button><button className="button button-secondary" onClick={() => setSaved(!saved)}>{saved ? <Check size={17} /> : <Star size={17} />} {saved ? "已设为默认" : "设为默认角色"}</button></div>
      </section>

      <div className="ds-width content-grid">
        <section className="panel token-panel"><div className="section-heading"><div><span className="kicker">01 / Tokens</span><h2>颜色与状态</h2></div><Settings2 size={20} className="muted-icon" /></div><div className="swatches">{[["--accent", "青黛主色"], ["--accent-soft", "主色浅底"], ["--ok", "完成"], ["--warn", "候补"], ["--bad", "冲突"]].map(([token, label]) => <div className="swatch" key={token}><span style={{ background: `var(${token})` }} /><div><strong>{label}</strong><small>{token}</small></div></div>)}</div><div className="state-row"><span className="status status-ok"><Check size={14} /> 已满员</span><span className="status status-warn"><Clock3 size={14} /> 候补中</span><span className="status status-bad"><TriangleAlert size={14} /> 时间冲突</span></div></section>

        <section className="panel component-panel"><div className="section-heading"><div><span className="kicker">02 / Components</span><h2>操作反馈</h2></div><Copy size={20} className="muted-icon" /></div><div className="button-row"><button className="button button-primary">报名这场 <ChevronRight size={16} /></button><button className="button button-secondary">查看详情</button><button className="button button-danger">取消报名</button></div><div className="field-row"><label className="field"><span>搜索玩家</span><div className="input-wrap"><Search size={17} /><input placeholder="角色名或流派" /></div></label><label className="field"><span>场次状态</span><select defaultValue="all"><option value="all">全部状态</option><option>缺人</option><option>已满员</option></select></label></div></section>

        <section className="panel schedule-panel"><div className="section-heading"><div><span className="kicker">03 / Schedule Card</span><h2>副本场次</h2></div><span className="status status-warn">缺 2 人</span></div><div className="event-card"><div className="event-top"><div><span className="event-time">今晚 20:00</span><h3>无名剑 · 试炼场</h3></div><span className="event-count">8 / 10</span></div><div className="role-list">{roles.map(({ label, icon: Icon, tone }, index) => <div className="role-line" key={label}><span className={`role-label ${tone}`}><Icon size={15} /> {label}</span><div className="slot-stack">{Array.from({ length: index === 0 ? 1 : index === 1 ? 2 : 5 }).map((_, i) => <span className="slot-avatar" key={i}>{["玄", "青", "鹿", "云", "砚", "昭", "舟"][i]}</span>)}{index === 2 && <><span className="slot-empty" /><span className="slot-empty" /></>}</div></div>)}</div><div className="event-foot"><span>自然周 · 北京时间</span><button className="text-button" onClick={() => setDrawerOpen(true)}>场次详情 <ChevronRight size={15} /></button></div></div></section>

        <section className="panel role-panel"><div className="section-heading"><div><span className="kicker">04 / Character</span><h2>我的角色</h2></div><UserRound size={20} className="muted-icon" /></div><div className="character-card"><div className="character-avatar">云</div><div className="character-info"><div className="character-title"><strong>云中鹿</strong><span className="default-tag"><Star size={12} /> 默认</span></div><span>鹅力 6842 · 流派：鸣金虹、牵丝霜</span><small>周二、周四 · 20:00 后在线</small></div><button className="icon-button" aria-label="角色设置"><Settings2 size={18} /></button></div></section>
      </div>

      <section className="panel week-panel ds-width" aria-label="周日历泳道示例">
        <div className="section-heading"><div><span className="kicker">05 / Week Calendar</span><h2>副本周日历</h2><p className="week-caption">2026.10.12 — 10.18 · 北京时间 · 示例数据</p></div><CalendarDays size={22} className="muted-icon" /></div>
        <div className="week-scroll"><div className="week-board">
          {weekDays.map((day, index) => <section className="day-lane" key={day} aria-label={day}>
            <div className="day-heading"><strong>{day}</strong><span>10 / {12 + index}</span></div>
            <div className="day-events">{weekEvents.filter(event => event.day === index).map(event => <button className="week-event" key={event.id} onClick={() => { setSelectedEvent(event); setDrawerOpen(true); }} aria-label={`${event.date} ${event.time} ${event.name}场次详情`}>
              <span className="week-event-time"><Clock3 size={13} />{event.time}</span><strong>{event.name}</strong><span className="status status-warn">缺 2 输出</span><span className="week-event-count">8 / 10 人 <ChevronRight size={14} /></span>
            </button>)}{!weekEvents.some(event => event.day === index) && <p className="lane-empty">这天没有安排副本</p>}</div>
          </section>)}
        </div></div>
        <p className="week-caption">点击场次查看角色详情；桌面七天等宽排列，移动端按日期纵向排列。</p>
      </section>

      <section className="motion-strip ds-width"><div><span className="kicker">06 / Motion</span><h2>动效只回答一个问题：发生了什么？</h2><p>悬停、打开、成功和冲突都有明确反馈；页面进入不做装饰性动画。</p></div><div className="motion-demo"><span className="pulse-dot" /><span>同步中</span><span className="motion-line" /></div></section>

      <footer className="ds-width footer-note"><LockKeyhole size={14} /> 非官方玩家工具，与游戏运营方无关</footer>

      {drawerOpen && <div className="drawer-layer" role="presentation" onClick={() => setDrawerOpen(false)}><aside className="drawer" role="dialog" aria-modal="true" aria-label="场次详情" onClick={(event) => event.stopPropagation()}>
        <div className="drawer-header"><div><span className="kicker">{selectedEvent.date} · {selectedEvent.time}</span><h2>{selectedEvent.name}</h2></div><button className="icon-button" onClick={() => setDrawerOpen(false)} aria-label="关闭"><X size={19} /></button></div>
        <div className="drawer-status"><span className="status status-warn">缺 2 输出</span><span>北京时间 · 预计 90 分钟</span></div>
        <div className="drawer-section"><h3>已参加角色 <span className="participant-total">8 / 10 人</span></h3><div className="participant-list">{participants.map(person => <div className="participant" key={person.name}><span className="character-avatar small">{person.name[0]}</span><div className="participant-info"><div><strong>{person.name}</strong><span className={roles.find(role => role.label === person.position)?.tone}>{person.position}</span></div><small>鹅力 {person.power}</small><p>流派：{person.schools}</p></div></div>)}</div></div>
        <div className="drawer-section"><h3>队伍需求</h3><div className="drawer-roles">{roles.map(({ label, icon: Icon, tone }, i) => <div className={`drawer-role ${tone}`} key={label}><Icon size={18} /><span>{label}</span><strong>{i === 0 ? "1 / 1" : i === 1 ? "2 / 2" : "5 / 7"}</strong><small>{i === 2 ? "还缺 2 人" : "已满"}</small></div>)}</div></div>
        <div className="drawer-section"><h3>选择角色后报名</h3><label className="field"><span>本场角色</span><select defaultValue="云中鹿"><option value="云中鹿">云中鹿 · 默认 · 鹅力 6842 · 鸣金虹、牵丝霜</option><option value="云外山">云外山 · 鹅力 6520 · 鸣金影</option></select></label><label className="field signup-position"><span>本场定位</span><select defaultValue="输出"><option>输出</option><option>奶妈</option><option>承伤</option></select></label><p className="signup-note">报名即确认占位；定位已满时加入候补。此处为交互样式示例。</p><button className="button button-primary full-button" onClick={() => setDrawerOpen(false)}>报名这场 <Check size={17} /></button></div>
      </aside></div>}
    </main>
  );
}
