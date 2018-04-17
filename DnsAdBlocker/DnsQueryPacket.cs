using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DnsAdBlocker
{

    public class DnsQuery
    {
        private string _Url;

        public string Url
        {
            get { return _Url; }
            set { _Url = value; }
        }

        private UInt16 _Type;

        public UInt16 Type
        {
            get { return _Type; }
            set { _Type = value; }
        }

        private UInt16 _Class;

        public UInt16 Class
        {
            get { return _Class; }
            set { _Class = value; }
        }

    }


    public class DnsQueryPacket
    {
        private UInt16 _TransactionId;

        public UInt16 TransactionId
        {
            get { return _TransactionId; }
            set { _TransactionId = value; }
        }

        private UInt16 _Flags;

        public UInt16 Flags
        {
            get { return _Flags; }
            set { _Flags = value; }
        }

        private UInt16 _Questions;

        public UInt16 Questions
        {
            get { return _Questions; }
            set { _Questions = value; }
        }

        private UInt16 _Answers;

        public UInt16 Answers
        {
            get { return _Answers; }
            set { _Answers = value; }
        }

        private UInt16 _AuthorityRRs;

        public UInt16 AuthorityRRs
        {
            get { return _AuthorityRRs; }
            set { _AuthorityRRs = value; }
        }

        private UInt16 _AdditionalRRs;

        public UInt16 AdditionalRRs
        {
            get { return _AdditionalRRs; }
            set { _AdditionalRRs = value; }
        }


        private List<DnsQuery> _Queries;

        public List<DnsQuery> Queries
        {
            get { return _Queries; }
            set { _Queries = value; }
        }


        string FormatDnsQuery(char[] queryArray)
        {
            StringBuilder sb = new StringBuilder(512);

            for(int i = 0; i < queryArray.Length; i++)
            {
                int count = (int)queryArray[i];

                if(queryArray[i] == '\0' || count == 0)
                    break;
                i++;
                for(int j = i; j < i + count; j++)
                {
                    sb.Append(queryArray[j]);                    
                }

                i = i + count-1;

                if( i+1 < queryArray.Length && queryArray[i+1] != '\0')
                {
                    sb.Append(".");
                }                
            }


            string FinalString = sb.ToString();

            //var splits = FinalString.Split(".com", StringSplitOptions.RemoveEmptyEntries);
            //foreach (var item in splits)
            //{
            //    Console.WriteLine("\t\t{0}", item);
            //}

            return FinalString;

        }

        public DnsQueryPacket(DnsPayload payload)
        {
            int index = 0;
            byte[] Temp = new byte[2];

            Temp[0] = payload.Query[1];
            Temp[1] = payload.Query[0];
            this.TransactionId = BitConverter.ToUInt16(Temp, 0);
            index += 2;


            Temp[0] = payload.Query[3];
            Temp[1] = payload.Query[2];
            this.Flags = BitConverter.ToUInt16(Temp, 0);
            index += 2;

            Temp = new byte[2];
            Temp[0] = payload.Query[5];
            Temp[1] = payload.Query[4];
            this.Questions = BitConverter.ToUInt16(Temp, 0);
            index += 2;


            Temp[0] = payload.Query[7];
            Temp[1] = payload.Query[6];
            this.Answers = BitConverter.ToUInt16(Temp, 0);
            index += 2;

            Temp[0] = payload.Query[9];
            Temp[1] = payload.Query[8];
            this.AuthorityRRs = BitConverter.ToUInt16(Temp, 0);
            index += 2;


            Temp[0] = payload.Query[11];
            Temp[1] = payload.Query[10];
            this.AdditionalRRs = BitConverter.ToUInt16(Temp, 0);
            index += 2;

            this.Queries = new List<DnsQuery>(this.Questions);

            for(int i = 0; i < this.Questions && index < payload.Query.Length; i++)
            {
                DnsQuery query = new DnsQuery();

                char[] queryArray = new char[payload.Query.Length - index];
                int nqueryArray = 0;

                for(int j = index; j < payload.Query.Length; j++)
                {
                    queryArray[nqueryArray++] = Convert.ToChar(payload.Query[index++]);
                    if(Convert.ToChar(payload.Query[j]) == '\0')
                    {
                        break;
                    }
                }

                query.Url = FormatDnsQuery(queryArray);

                Temp[0] = payload.Query[index+1];
                Temp[1] = payload.Query[index];
                query.Type = BitConverter.ToUInt16(Temp, 0);
                index += 2;

                Temp[0] = payload.Query[index + 1];
                Temp[1] = payload.Query[index];
                query.Class = BitConverter.ToUInt16(Temp, 0);
                index += 2;

                Queries.Add(query);
            }
        }


        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(2048);

            sb.AppendFormat("TxId: 0x{0:x4} " + "Flags: 0x{1:x4} " + "Questions: {2} " + "Answers: {3} " + "Auth RR: {4} " + "Add RRs: {5}",
                this.TransactionId, this.Flags, this.Questions, this.Answers, this.AuthorityRRs, this.AdditionalRRs);

            sb.Append("\n");
            foreach(var item in this.Queries)
            {
                sb.AppendFormat("\tQuery:: Url: {0}, Type {1}, Class {2}.", item.Url, item.Type, item.Class);
            }

            return sb.ToString();
        }



    }
}
