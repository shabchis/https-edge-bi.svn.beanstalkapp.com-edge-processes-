using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Edge.Processes.SchedulingHost
{
	//public class ImporterServiceTester : PipelineService
	//{
	//    protected override Core.Services.ServiceOutcome DoPipelineWork()
	//    {
			
	//        //Initalize
	//        Delivery = this.NewDelivery();
	//        this.Delivery.Outputs.Add(new DeliveryOutput(){
	//            Signature= Delivery.CreateSignature(String.Format("ImporterTest-[{0}]-[{1}]",
	//          this.Instance.AccountID,
	//          this.TimePeriod.ToAbsolute())), Account=new Data.Objects.Account(){ ID=Instance.AccountID} ,
	//         Channel=new Data.Objects.Channel() { ID=-1}});

	//    //	this.HandleConflicts(importManager, DeliveryConflictBehavior.Ignore);

	//        ReportProgress(0.1);

	//        //retrive
	//        Thread.Sleep(10000);
	//        ReportProgress(0.3);

	//    //	Proccess
	//        Thread.Sleep(10000);
	//        ReportProgress(0.6);


	//    //	commit
	//        Thread.Sleep(10000);
	//        ReportProgress(0.9);
			
	//        return Core.Services.ServiceOutcome.Success;


	//    }
	//}
}
