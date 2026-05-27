-- ============================================================
-- 生产库数据导出脚本 (纯 SQL 版本，SQL Window 可直接执行)
-- 每段 SELECT 生成对应表的 INSERT 语句
-- 复制输出结果到测试库执行
-- 日期范围：近 30 天（USERS, DEPT_DICT, PAT_MASTER_INDEX 为全量）
-- ============================================================

-- ============================================================
-- 1. OUTP_MR (门诊病历记录, 来源: HIS门诊医生站数据结构.txt §1.1)
-- ============================================================
SELECT 'INSERT INTO outp_mr (PATIENT_ID, VISIT_DATE, VISIT_NO, ILLNESS_DESC, ANAMNESIS, FAMILY_ILL, MARRITAL, INDIVIDUDL, MENSES, MED_HISTORY, BODY_EXAM, DIAG_DESC, ADVICE, DOCTOR) VALUES ('
    || '''' || PATIENT_ID     || ''', '
    || 'TO_DATE(''' || TO_CHAR(VISIT_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    ||        VISIT_NO        || ', '
    || '''' || REPLACE(ILLNESS_DESC, '''', '''''') || ''', '
    || '''' || REPLACE(ANAMNESIS,    '''', '''''') || ''', '
    || '''' || REPLACE(FAMILY_ILL,   '''', '''''') || ''', '
    || '''' || MARRITAL             || ''', '
    || '''' || REPLACE(INDIVIDUDL,   '''', '''''') || ''', '
    || '''' || REPLACE(MENSES,       '''', '''''') || ''', '
    || '''' || REPLACE(MED_HISTORY,  '''', '''''') || ''', '
    || '''' || REPLACE(BODY_EXAM,    '''', '''''') || ''', '
    || '''' || REPLACE(DIAG_DESC,    '''', '''''') || ''', '
    || '''' || REPLACE(ADVICE,       '''', '''''') || ''', '
    || '''' || REPLACE(DOCTOR,       '''', '''''') || ''''
    || ');'
FROM outp_mr
WHERE VISIT_DATE >= TRUNC(SYSDATE) - 30;

-- ============================================================
-- 2. CLINIC_MASTER (就诊记录, 来源: DBSCHM.txt §10.5)
-- ============================================================
SELECT 'INSERT INTO clinic_master (VISIT_DATE, VISIT_NO, CLINIC_LABEL, VISIT_TIME_DESC, SERIAL_NO, PATIENT_ID, NAME, NAME_PHONETIC, SEX, AGE, IDENTITY, CHARGE_TYPE, INSURANCE_TYPE, INSURANCE_NO, UNIT_IN_CONTRACT, CLINIC_TYPE, FIRST_VISIT_INDICATOR, VISIT_DEPT, VISIT_SPECIAL_CLINIC, DOCTOR, MR_PROVIDED_INDICATOR, REGISTRATION_STATUS, REGISTERING_DATE, SYMPTOM, REGIST_FEE, CLINIC_FEE, OTHER_FEE, CLINIC_CHARGE, OPERATOR, RETURNED_DATE, RETURNED_OPERATOR) VALUES ('
    || 'TO_DATE(''' || TO_CHAR(VISIT_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    ||        VISIT_NO              || ', '
    || '''' || CLINIC_LABEL         || ''', '
    || '''' || VISIT_TIME_DESC      || ''', '
    ||        SERIAL_NO             || ', '
    || '''' || PATIENT_ID           || ''', '
    || '''' || REPLACE(NAME,           '''', '''''') || ''', '
    || '''' || REPLACE(NAME_PHONETIC,  '''', '''''') || ''', '
    || '''' || SEX                  || ''', '
    ||        AGE                   || ', '
    || '''' || IDENTITY             || ''', '
    || '''' || CHARGE_TYPE          || ''', '
    || '''' || INSURANCE_TYPE       || ''', '
    || '''' || INSURANCE_NO         || ''', '
    || '''' || UNIT_IN_CONTRACT     || ''', '
    || '''' || CLINIC_TYPE          || ''', '
    ||        FIRST_VISIT_INDICATOR || ', '
    || '''' || VISIT_DEPT           || ''', '
    || '''' || VISIT_SPECIAL_CLINIC || ''', '
    || '''' || REPLACE(DOCTOR, '''', '''''') || ''', '
    ||        MR_PROVIDED_INDICATOR || ', '
    ||        REGISTRATION_STATUS   || ', '
    || 'TO_DATE(''' || TO_CHAR(REGISTERING_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || REPLACE(SYMPTOM, '''', '''''') || ''', '
    ||        REGIST_FEE            || ', '
    ||        CLINIC_FEE            || ', '
    ||        OTHER_FEE             || ', '
    ||        CLINIC_CHARGE         || ', '
    || '''' || OPERATOR             || ''', '
    || 'TO_DATE(''' || TO_CHAR(RETURNED_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || RETURNED_OPERATOR    || ''''
    || ');'
FROM clinic_master
WHERE VISIT_DATE >= TRUNC(SYSDATE) - 30;

-- ============================================================
-- 3. USERS (用户记录, 来源: DBSCHM.txt §1.16)
-- ============================================================
SELECT 'INSERT INTO users (DB_USER, USER_ID, USER_NAME, USER_DEPT, CREATE_DATE) VALUES ('
    || '''' || DB_USER              || ''', '
    || '''' || USER_ID              || ''', '
    || '''' || REPLACE(USER_NAME, '''', '''''') || ''', '
    || '''' || USER_DEPT            || ''', '
    || 'TO_DATE(''' || TO_CHAR(CREATE_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD'')'
    || ');'
FROM users;

-- ============================================================
-- 4. DEPT_DICT (科室字典, 来源: DBSCHM.txt §2.6)
-- ============================================================
SELECT 'INSERT INTO dept_dict (SERIAL_NO, DEPT_CODE, DEPT_NAME, DEPT_ALIAS, CLINIC_ATTR, OUTP_OR_INP, INTERNAL_OR_SERGERY, INPUT_CODE) VALUES ('
    ||        SERIAL_NO            || ', '
    || '''' || DEPT_CODE           || ''', '
    || '''' || REPLACE(DEPT_NAME,  '''', '''''') || ''', '
    || '''' || REPLACE(DEPT_ALIAS, '''', '''''') || ''', '
    ||        CLINIC_ATTR          || ', '
    ||        OUTP_OR_INP          || ', '
    ||        INTERNAL_OR_SERGERY  || ', '
    || '''' || INPUT_CODE          || ''''
    || ');'
FROM dept_dict;

-- ============================================================
-- 5. PAT_MASTER_INDEX (病人主索引, 来源: DBSCHM.txt §9.1)
-- ============================================================
SELECT 'INSERT INTO pat_master_index (PATIENT_ID, INP_NO, NAME, NAME_PHONETIC, SEX, DATE_OF_BIRTH, BIRTH_PLACE, CITIZENSHIP, NATION, ID_NO, IDENTITY, CHARGE_TYPE, UNIT_IN_CONTRACT, MAILING_ADDRESS, ZIP_CODE, PHONE_NUMBER_HOME, PHONE_NUMBER_BUSINESS, NEXT_OF_KIN, RELATIONSHIP, NEXT_OF_KIN_ADDR, NEXT_OF_KIN_ZIP_CODE, NEXT_OF_KIN_PHONE, LAST_VISIT_DATE, VIP_INDICATOR, CREATE_DATE, OPERATOR) VALUES ('
    || '''' || PATIENT_ID              || ''', '
    || '''' || INP_NO                  || ''', '
    || '''' || REPLACE(NAME,             '''', '''''') || ''', '
    || '''' || REPLACE(NAME_PHONETIC,    '''', '''''') || ''', '
    || '''' || SEX                     || ''', '
    || 'TO_DATE(''' || TO_CHAR(DATE_OF_BIRTH, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || BIRTH_PLACE             || ''', '
    || '''' || CITIZENSHIP             || ''', '
    || '''' || NATION                  || ''', '
    || '''' || ID_NO                   || ''', '
    || '''' || IDENTITY                || ''', '
    || '''' || CHARGE_TYPE             || ''', '
    || '''' || UNIT_IN_CONTRACT        || ''', '
    || '''' || REPLACE(MAILING_ADDRESS,      '''', '''''') || ''', '
    || '''' || ZIP_CODE                || ''', '
    || '''' || PHONE_NUMBER_HOME       || ''', '
    || '''' || PHONE_NUMBER_BUSINESS   || ''', '
    || '''' || REPLACE(NEXT_OF_KIN,          '''', '''''') || ''', '
    || '''' || RELATIONSHIP            || ''', '
    || '''' || REPLACE(NEXT_OF_KIN_ADDR,     '''', '''''') || ''', '
    || '''' || NEXT_OF_KIN_ZIP_CODE    || ''', '
    || '''' || NEXT_OF_KIN_PHONE       || ''', '
    || 'TO_DATE(''' || TO_CHAR(LAST_VISIT_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    ||        VIP_INDICATOR            || ', '
    || 'TO_DATE(''' || TO_CHAR(CREATE_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || OPERATOR                || ''''
    || ');'
FROM pat_master_index;

-- ============================================================
-- 6. PATS_IN_HOSPITAL (在院病人记录, 来源: DBSCHM.txt §11.3)
-- ============================================================
SELECT 'INSERT INTO pats_in_hospital (PATIENT_ID, VISIT_ID, WARD_CODE, DEPT_CODE, BED_NO, ADMISSION_DATE_TIME, ADM_WARD_DATE_TIME, DIAGNOSIS, PATIENT_CONDITION, NURSING_CLASS, DOCTOR_IN_CHARGE, OPERATING_DATE, BILLING_DATE_TIME, PREPAYMENTS, TOTAL_COSTS, TOTAL_CHARGES, GUARANTOR, GUARANTOR_ORG, GUARANTOR_PHONE_NUM, BILL_CHECKED_DATE_TIME, SETTLED_INDICATOR) VALUES ('
    || '''' || PATIENT_ID              || ''', '
    ||        VISIT_ID                 || ', '
    || '''' || WARD_CODE               || ''', '
    || '''' || DEPT_CODE               || ''', '
    ||        BED_NO                   || ', '
    || 'TO_DATE(''' || TO_CHAR(ADMISSION_DATE_TIME, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || 'TO_DATE(''' || TO_CHAR(ADM_WARD_DATE_TIME, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || REPLACE(DIAGNOSIS,        '''', '''''') || ''', '
    || '''' || PATIENT_CONDITION       || ''', '
    || '''' || NURSING_CLASS           || ''', '
    || '''' || REPLACE(DOCTOR_IN_CHARGE, '''', '''''') || ''', '
    || 'TO_DATE(''' || TO_CHAR(OPERATING_DATE, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || 'TO_DATE(''' || TO_CHAR(BILLING_DATE_TIME, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    ||        PREPAYMENTS              || ', '
    ||        TOTAL_COSTS              || ', '
    ||        TOTAL_CHARGES            || ', '
    || '''' || REPLACE(GUARANTOR,        '''', '''''') || ''', '
    || '''' || REPLACE(GUARANTOR_ORG,    '''', '''''') || ''', '
    || '''' || GUARANTOR_PHONE_NUM     || ''', '
    || 'TO_DATE(''' || TO_CHAR(BILL_CHECKED_DATE_TIME, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    ||        SETTLED_INDICATOR        || ''
    || ');'
FROM pats_in_hospital
WHERE ADMISSION_DATE_TIME >= TRUNC(SYSDATE) - 30;

-- ============================================================
-- 7. PAT_VISIT (病人住院主记录, 来源: DBSCHM.txt §9.2)
-- 列数较多(64列), 导出常用核心列
-- ============================================================
SELECT 'INSERT INTO pat_visit (PATIENT_ID, VISIT_ID, DEPT_ADMISSION_TO, ADMISSION_DATE_TIME, DEPT_DISCHARGE_FROM, DISCHARGE_DATE_TIME, OCCUPATION, MARITAL_STATUS, IDENTITY, CHARGE_TYPE, WORKING_STATUS, INSURANCE_TYPE, INSURANCE_NO, SERVICE_AGENCY, MAILING_ADDRESS, NEXT_OF_KIN, RELATIONSHIP, PATIENT_CLASS, ADMISSION_CAUSE, CONSULTING_DOCTOR, ADMITTED_BY, DOCTOR_IN_CHARGE, DISCHARGE_DISPOSITION, TOTAL_COSTS, TOTAL_PAYMENTS, DIRECTOR, ATTENDING_DOCTOR) VALUES ('
    || '''' || PATIENT_ID              || ''', '
    ||        VISIT_ID                 || ', '
    || '''' || DEPT_ADMISSION_TO       || ''', '
    || 'TO_DATE(''' || TO_CHAR(ADMISSION_DATE_TIME, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || DEPT_DISCHARGE_FROM     || ''', '
    || 'TO_DATE(''' || TO_CHAR(DISCHARGE_DATE_TIME, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || OCCUPATION              || ''', '
    || '''' || MARITAL_STATUS          || ''', '
    || '''' || IDENTITY                || ''', '
    || '''' || CHARGE_TYPE             || ''', '
    ||        WORKING_STATUS           || ', '
    || '''' || INSURANCE_TYPE          || ''', '
    || '''' || INSURANCE_NO            || ''', '
    || '''' || REPLACE(SERVICE_AGENCY,  '''', '''''') || ''', '
    || '''' || REPLACE(MAILING_ADDRESS, '''', '''''') || ''', '
    || '''' || REPLACE(NEXT_OF_KIN,     '''', '''''') || ''', '
    || '''' || RELATIONSHIP            || ''', '
    || '''' || PATIENT_CLASS           || ''', '
    || '''' || REPLACE(ADMISSION_CAUSE, '''', '''''') || ''', '
    || '''' || REPLACE(CONSULTING_DOCTOR, '''', '''''') || ''', '
    || '''' || REPLACE(ADMITTED_BY,      '''', '''''') || ''', '
    || '''' || REPLACE(DOCTOR_IN_CHARGE, '''', '''''') || ''', '
    || '''' || DISCHARGE_DISPOSITION   || ''', '
    ||        TOTAL_COSTS              || ', '
    ||        TOTAL_PAYMENTS           || ', '
    || '''' || REPLACE(DIRECTOR,         '''', '''''') || ''', '
    || '''' || REPLACE(ATTENDING_DOCTOR, '''', '''''') || ''''
    || ');'
FROM pat_visit
WHERE ADMISSION_DATE_TIME >= TRUNC(SYSDATE) - 30;

-- ============================================================
-- 8. INP_BILL_DETAIL (住院病人费用明细, 来源: DBSCHM.txt §17.1)
-- ============================================================
SELECT 'INSERT INTO inp_bill_detail (PATIENT_ID, VISIT_ID, ITEM_NO, ITEM_CLASS, ITEM_NAME, ITEM_CODE, ITEM_SPEC, AMOUNT, UNITS, ORDERED_BY, PERFORMED_BY, COSTS, CHARGES, BILLING_DATE_TIME, OPERATOR_NO, RCPT_NO) VALUES ('
    || '''' || PATIENT_ID              || ''', '
    ||        VISIT_ID                 || ', '
    ||        ITEM_NO                  || ', '
    || '''' || ITEM_CLASS              || ''', '
    || '''' || REPLACE(ITEM_NAME, '''', '''''') || ''', '
    || '''' || ITEM_CODE               || ''', '
    || '''' || ITEM_SPEC               || ''', '
    ||        AMOUNT                   || ', '
    || '''' || UNITS                   || ''', '
    || '''' || ORDERED_BY              || ''', '
    || '''' || PERFORMED_BY            || ''', '
    ||        COSTS                    || ', '
    ||        CHARGES                  || ', '
    || 'TO_DATE(''' || TO_CHAR(BILLING_DATE_TIME, 'YYYY-MM-DD') || ''', ''YYYY-MM-DD''), '
    || '''' || OPERATOR_NO             || ''', '
    || '''' || RCPT_NO                 || ''''
    || ');'
FROM inp_bill_detail
WHERE BILLING_DATE_TIME >= TRUNC(SYSDATE) - 30;
