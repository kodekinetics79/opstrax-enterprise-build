import 'dotenv/config';
import express from 'express';
import cors from 'cors';
const app=express(); app.use(cors()); app.use(express.json());
app.get('/health',(req,res)=>res.json({service:'zayra-node-ai',status:'ok'}));
app.post('/api/ai/insights',(req,res)=>{
 const {attendanceVariance=18.7,churnCount=12}=req.body||{};
 res.json({insights:[
  {type:'overtime',severity:'high',title:'High Overtime Alert',message:`Overtime is ${attendanceVariance}% higher than last month.`},
  {type:'churn',severity:'medium',title:'Employee Churn Risk',message:`${churnCount} employees may require retention follow-up.`},
  {type:'attendance',severity:'medium',title:'Attendance Anomaly',message:'7 employees show irregular time-in/time-out behavior.'}
 ]});
});
app.listen(process.env.PORT||5050,()=>console.log('Zayra Node AI service running'));
