const fs = require('fs');
const actors = JSON.parse(fs.readFileSync('D:/pick/Assets/Resources/Table/actor.json', 'utf8')).actors;
const missions = JSON.parse(fs.readFileSync('D:/pick/Assets/Resources/Table/mission.json', 'utf8')).missions;
function rng(seed) { return () => { seed ^= seed << 13; seed ^= seed >>> 17; seed ^= seed << 5; return (seed >>> 0) / 4294967296; }; }
const DT=.1, PULL_SECONDS=18, RUNS=100;
function simulate(stage, successRate, seed) {
  const random=rng(seed), boss=actors[stage-1];
  const pool=actors.filter(a=>a.stage>=Math.max(1,stage-15)&&a.stage<=Math.max(2,stage-1));
  const missionPool=actors.filter(a=>a.stage>=Math.max(1,Math.max(2,stage-1)-5)&&a.stage<=Math.max(2,stage-1));
  const slots=[null,null], queue=[], hits=[];
  let hp=boss.bossHp, pulls=0, combo=0, target=null, bossNext=Infinity, targetSearchAt=0, serial=0;
  let mission, missionTarget, missionProgress=0;
  function pickMission() {
    let roll=random()*missions.reduce((s,m)=>s+m.percent,0);
    mission=missions[missions.length-1];
    for(const m of missions) { roll-=m.percent; if(roll<=0){mission=m;break;} }
    missionTarget=missionPool[Math.floor(random()*missionPool.length)];missionProgress=0;
  }
  function enqueue(actor,count,time){for(let i=0;i<count;i++)queue.push({actor,time:time+1});}
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
  for(let t=0;t<14400;t+=DT){
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
    if(hp<=0)return {seconds:t,pulls,censored:false};
  }
  return {seconds:14400,pulls,censored:true};
}
const percentile=(values,p)=>values.slice().sort((a,b)=>a-b)[Math.floor((values.length-1)*p)];
const rows=[];
for(const boss of actors){
  const pool=actors.filter(a=>a.stage>=Math.max(1,boss.stage-15)&&a.stage<=Math.max(2,boss.stage-1));
  const meanDps=pool.reduce((s,a)=>s+a.allyAttackPower/a.allyAttackSpeed,0)/pool.length;
  for(const q of [.4,.6,.8]){
    const runs=Array.from({length:RUNS},(_,i)=>simulate(boss.stage,q,boss.stage*100000+Math.round(q*100)*1000+i+1));
    rows.push({stage:boss.stage,animal:boss.animalKey,boss_hp:boss.bossHp,boss_attack:boss.bossAttackPower,
      pool_min:pool[0].stage,pool_max:pool.at(-1).stage,mean_ally_dps:+meanDps.toFixed(2),
      two_average_allies_seconds:+(boss.bossHp/(2*meanDps)).toFixed(1),assumed_success_rate:q,
      assumed_pull_seconds:PULL_SECONDS,runs:RUNS,
      model_median_minutes:+(percentile(runs.map(r=>r.seconds),.5)/60).toFixed(2),
      model_p10_minutes:+(percentile(runs.map(r=>r.seconds),.1)/60).toFixed(2),
      model_p90_minutes:+(percentile(runs.map(r=>r.seconds),.9)/60).toFixed(2),
      model_median_pulls:percentile(runs.map(r=>r.pulls),.5),censored:runs.filter(r=>r.censored).length});
  }
}
const out='D:/pick/_report/2026-09-12_difficulty_scenarios.csv';
fs.writeFileSync(out,Object.keys(rows[0]).join(',')+'\n'+rows.map(r=>Object.values(r).join(',')).join('\n')+'\n');
for(const row of rows)if(row.assumed_success_rate===.6)console.log(JSON.stringify(row));
console.log('SENSITIVITY');
for(const row of rows)if([3,12,24,36].includes(row.stage))console.log(JSON.stringify(row));
console.log('OUTPUT '+out);
