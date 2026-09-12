const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const beforePath = path.join(__dirname, '2026-09-12_wave_balance_before.json');
const actorPath = path.join(root, 'Assets/Resources/Table/actor.json');
const missionPath = path.join(root, 'Assets/Resources/Table/mission.json');
if (!fs.existsSync(beforePath)) fs.copyFileSync(actorPath, beforePath);
const before = JSON.parse(fs.readFileSync(beforePath, 'utf8'));
const actors = JSON.parse(JSON.stringify(before.actors));
const missions = JSON.parse(fs.readFileSync(missionPath, 'utf8')).missions;
const targets = [0,0,0,6,6.8,7.6,8.3,8.8,9,9,8.9,8.8,8.8,9.3,10.2,11.3,12.4,13.3,13.9,14.1,14.1,14,13.8,13.6,13.5,13.7,14.3,15.3,16.7,18.1,19.3,20.3,21.1,21.8,22.4,23];
const DT = .1, PULL_SECONDS = 18, RUNS = 300;
function rng(seed) { return () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed >>> 0) / 4294967296; }; }
const percentile = (values, p) => values.slice().sort((a,b) => a-b)[Math.floor((values.length-1)*p)];
function poolFor(stage) { return actors.filter(a => a.stage >= Math.max(1,stage-15) && a.stage <= Math.max(2,stage-1)); }
function simulate(stage, successRate, seed, limit=14400, startingAllies=[]) {
  const random=rng(seed), boss=actors[stage-1], pool=poolFor(stage);
  const missionPool=actors.filter(a=>a.stage>=Math.max(1,Math.max(2,stage-1)-5)&&a.stage<=Math.max(2,stage-1));
  const slots=[null,null], queue=[], hits=[];
  let hp=boss.bossHp, pulls=0, combo=0, target=null, bossNext=Infinity, targetSearchAt=0, serial=0;
  let mission, missionTarget, missionProgress=0;
  function pickMission() {
    const previous = mission && mission.mission_type === 'actor' ? missionTarget : null;
    let roll=random()*missions.reduce((s,m)=>s+m.percent,0);
    mission=missions[missions.length-1];
    for(const m of missions) { roll-=m.percent; if(roll<=0){mission=m;break;} }
    const eligible = stage >= 4 && missionPool.length > 1 ? missionPool.filter(a=>a!==previous) : missionPool;
    missionTarget=eligible[Math.floor(random()*eligible.length)];missionProgress=0;
  }
  function enqueue(actor,count,time){for(let i=0;i<count;i++)queue.push({actor,time:time+1});}
  for(const actor of startingAllies) enqueue(actor,1,-1);
  function progress(actor,count,isBox,time){
    if((mission.mission_type==='actor'&&!isBox&&missionTarget.animalKey===actor.animalKey)||
       (mission.mission_type==='randombox'&&isBox)) {
      missionProgress+=count;
      if(missionProgress>=mission.collection_count){
        if(mission.mission_type==='actor')enqueue(missionTarget,mission.reward_count,time);
        pickMission();
      }
    }
  }
  pickMission();
  let nextPull=PULL_SECONDS;
  for(let t=0;t<limit;t+=DT){
    if(t+1e-6>=nextPull){
      nextPull+=PULL_SECONDS;pulls++;
      if(random()<successRate){
        combo++;
        const isBox=random()<1.5/21;
        if(isBox)progress(null,1,true,t);
        else {
          const actor=pool[Math.floor(random()*pool.length)];
          const golden=random()<1.5/(21-1.5);
          const count=(golden?3:1)+(combo>=5?3:combo===4?2:combo===3?1:0);
          enqueue(actor,count,t);progress(actor,count,false,t);
        }
      } else combo=0;
    }
    for(let i=0;i<2;i++){
      if(slots[i]&&slots[i].hp<=0&&t>=slots[i].deadUntil)slots[i]=null;
      if(!slots[i]&&queue.length&&queue[0].time<=t){
        const entry=queue.shift();
        slots[i]={id:++serial,actor:entry.actor,hp:entry.actor.allyHp,ready:t+2,next:t+2+entry.actor.allyAttackSpeed};
      }
    }
    for(const ally of slots)if(ally&&ally.hp>0&&t>=ally.next){
      hits.push({time:t+.4,damage:ally.actor.allyAttackPower});ally.next=t+ally.actor.allyAttackSpeed;
    }
    if(!target&&t>=targetSearchAt){
      target=slots.find(a=>a&&a.hp>0&&a.ready<=t)||null;
      if(target)bossNext=t+boss.bossAttackSpeed+.4;
    }
    if(target&&t>=bossNext){
      target.hp-=boss.bossAttackPower;
      if(target.hp<=0){target.deadUntil=t+1;target=null;targetSearchAt=t+1.3;bossNext=Infinity;}
      else bossNext=t+boss.bossAttackSpeed;
    }
    for(let i=hits.length-1;i>=0;i--)if(hits[i].time<=t){hp-=hits[i].damage;hits.splice(i,1);}
    if(hp<=0)return {seconds:t,pulls,censored:false,damage:boss.bossHp-hp};
  }
  return {seconds:limit,pulls,censored:true,damage:boss.bossHp-hp};
}
// Calibrate HP against supply-limited combat; do not speed up boss attacks by species tier.
for(const boss of actors.filter(a=>a.stage>=4)) {
  const meanHp=poolFor(boss.stage).reduce((s,a)=>s+a.allyHp,0)/poolFor(boss.stage).length;
  boss.bossAttackSpeed=+(1.5-(boss.stage-4)/32*.2).toFixed(2);
  boss.bossAttackPower=Math.round(meanHp*boss.bossAttackSpeed/12);
  boss.bossHp=1000000000;
  const damage=Array.from({length:RUNS},(_,i)=>simulate(boss.stage,.6,boss.stage*100000+i+1,targets[boss.stage-1]*60).damage);
  boss.bossHp=Math.max(100,Math.round(percentile(damage,.5)/50)*50);
  boss.clearCoinReward=500+Math.round((targets[boss.stage-1]-6)*.7)*100+([9,20,36].includes(boss.stage)?300:0);
}
const candidate={...before,actors};
fs.writeFileSync(path.join(__dirname,'2026-09-12_wave_balance_candidate.json'),JSON.stringify(candidate,null,2)+'\n');
const rows=[];
for(const boss of actors) {
  for(const q of [.4,.6,.8]) {
    // Independent validation seeds, not the calibration trajectories.
    const runs=Array.from({length:RUNS},(_,i)=>simulate(boss.stage,q,10000000+boss.stage*100000+Math.round(q*100)*1000+i+1));
    rows.push({stage:boss.stage,animal:boss.animalKey,old_hp:before.actors[boss.stage-1].bossHp,boss_hp:boss.bossHp,
      boss_attack:boss.bossAttackPower,boss_interval:boss.bossAttackSpeed,clear_coins:boss.clearCoinReward||500,
      target_minutes:targets[boss.stage-1]||'',assumed_success_rate:q,assumed_pull_seconds:PULL_SECONDS,runs:RUNS,
      median_minutes:+(percentile(runs.map(r=>r.seconds),.5)/60).toFixed(2),
      p10_minutes:+(percentile(runs.map(r=>r.seconds),.1)/60).toFixed(2),
      p90_minutes:+(percentile(runs.map(r=>r.seconds),.9)/60).toFixed(2),
      median_pulls:percentile(runs.map(r=>r.pulls),.5),censored:runs.filter(r=>r.censored).length});
  }
}
fs.writeFileSync(path.join(__dirname,'2026-09-12_wave_balance_scenarios.csv'),Object.keys(rows[0]).join(',')+'\n'+rows.map(r=>Object.values(r).join(',')).join('\n')+'\n');
for(const row of rows) if(row.assumed_success_rate===.6)console.log(JSON.stringify(row));
console.log('TWO STRONG STARTING ALLIES (not a full campaign simulation)');
for(const stage of [13,25]) {
  const strongest=poolFor(stage).slice().sort((a,b)=>b.allyAttackPower/b.allyAttackSpeed-a.allyAttackPower/a.allyAttackSpeed)[0];
  const runs=Array.from({length:RUNS},(_,i)=>simulate(stage,.6,90000000+stage*1000+i,14400,[strongest,strongest]));
  console.log(JSON.stringify({stage,median_minutes:+(percentile(runs.map(r=>r.seconds),.5)/60).toFixed(2)}));
}
